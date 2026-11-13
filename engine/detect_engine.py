"""
SafeEye 탐지 엔진 (Python)

윈폼 앱이 이 프로그램을 '자식 프로그램'으로 실행해서 사용한다.
주고받는 방식은 아주 단순하다: 한 줄에 JSON 하나.

[앱 -> 엔진]  (표준 입력)
    {"cmd": "start", "video": "영상경로", "model": "best.pt 경로", "conf": 0.5, "seconds": 3,
     "capture_dir": "captures 폴더", "save_capture": true}
        ※ "video" 에 "camera:0" 을 주면 웹캠(0번)으로 실시간 탐지한다. (끝나지 않으므로 stop 으로 멈춘다)
    {"cmd": "stop"}      지금 하던 탐지를 멈춘다
    {"cmd": "quit"}      엔진을 끝낸다

[엔진 -> 앱]  (표준 출력)
    {"type": "ready", "device": "cpu"}                       엔진 준비 끝
    {"type": "frame", "n": 120, "total": 900, "ms": 71, "helmet": 2, "head": 1,
     "warn": 1, "jpg": "<화면 이미지(base64)>"}               프레임 하나를 처리할 때마다
    {"type": "alert", "frame": 1284, "conf": 0.93, "duration": 3, "capture": "경로", "time": "2026-11-16 14:28:15"}
    {"type": "done", "frames": 450, "avg_ms": 71, "helmet_people": 2, "head_people": 1, "alerts": 1}
    {"type": "error", "message": "..."}

단독 시험(앱 없이):
    python detect_engine.py --video 영상.mp4 --model best.pt
"""
import argparse
import base64
import json
import queue
import sys
import threading
import time
from collections import Counter
from datetime import datetime
from pathlib import Path

import cv2
import numpy as np

# ---- 앱과 대화하는 통로 -----------------------------------------------------
# 라이브러리(ultralytics)가 print로 내보내는 글이 섞이면 앱이 헷갈리므로,
# 진짜 표준출력은 따로 보관하고 일반 print는 오류 출력(stderr)으로 돌린다.
sys.stdout.reconfigure(encoding="utf-8")
sys.stdin.reconfigure(encoding="utf-8")
_OUT = sys.stdout
sys.stdout = sys.stderr


def send(obj):
    """앱으로 JSON 한 줄을 보낸다."""
    _OUT.write(json.dumps(obj, ensure_ascii=False) + "\n")
    _OUT.flush()


# ---- 화면에 그릴 색 (BGR 순서) ----------------------------------------------
COLOR_HEAD = (26, 42, 255)     # 빨강: 안전모 미착용
COLOR_HELMET = (80, 176, 0)    # 초록: 안전모 착용


# =============================================================================
# 1) 아주 단순한 추적기
#    "이전 프레임의 박스와 가장 많이 겹치는 새 박스 = 같은 사람" 으로 본다.
#    사람마다 번호(id)를 붙여서, 미착용이 얼마나 이어지는지 사람별로 잰다.
# =============================================================================
def iou(a, b):
    """두 박스가 겹치는 정도 (0~1)."""
    x1, y1 = max(a[0], b[0]), max(a[1], b[1])
    x2, y2 = min(a[2], b[2]), min(a[3], b[3])
    inter = max(0, x2 - x1) * max(0, y2 - y1)
    area_a = (a[2] - a[0]) * (a[3] - a[1])
    area_b = (b[2] - b[0]) * (b[3] - b[1])
    union = area_a + area_b - inter
    return inter / union if union > 0 else 0.0


class Track:
    """추적 중인 사람 한 명."""

    def __init__(self, tid, det, step):
        self.id = tid
        self.box = det["box"]
        self.cls = det["cls"]            # 지금 판정: 'head'(미착용) 또는 'helmet'(착용)
        self.conf = det["conf"]
        self.hits = 1                    # 지금까지 잡힌 횟수
        self.votes = Counter([det["cls"]])
        self.last_step = step            # 마지막으로 잡힌 처리 번호
        self.head_since = None           # 미착용이 시작된 영상 시각(초)
        self.helmet_run = 0              # 연속으로 '착용'으로 보인 횟수
        self.alerted = False             # 이번 미착용 구간에서 이미 알림을 냈는가


class SimpleTracker:
    def __init__(self, iou_min=0.2, max_gap=15):
        self.iou_min = iou_min           # 이 이상 겹쳐야 같은 사람
        self.max_gap = max_gap           # 이만큼 못 보면 사라진 사람으로 처리
        self.tracks = []                 # 지금 추적 중인 사람들
        self.finished = []               # 사라진 사람들 (마지막에 인원 세기용)
        self.next_id = 1

    def update(self, dets, step):
        """이번 프레임의 탐지 결과를 받아 추적 목록을 갱신한다. 이번에 잡힌 Track 목록을 돌려준다."""
        pairs = []
        for ti, t in enumerate(self.tracks):
            for di, d in enumerate(dets):
                v = iou(t.box, d["box"])
                if v >= self.iou_min:
                    pairs.append((v, ti, di))
        pairs.sort(reverse=True)               # 많이 겹치는 짝부터 연결
        used_t, used_d, matched = set(), set(), []
        for v, ti, di in pairs:
            if ti in used_t or di in used_d:
                continue
            used_t.add(ti)
            used_d.add(di)
            t, d = self.tracks[ti], dets[di]
            t.box, t.cls, t.conf = d["box"], d["cls"], d["conf"]
            t.hits += 1
            t.votes[d["cls"]] += 1
            t.last_step = step
            matched.append(t)
        for di, d in enumerate(dets):          # 연결 못 한 박스 = 새로 나타난 사람
            if di not in used_d:
                t = Track(self.next_id, d, step)
                self.next_id += 1
                self.tracks.append(t)
                matched.append(t)
        alive = []
        for t in self.tracks:                  # 오래 안 보이면 사라진 사람
            if step - t.last_step > self.max_gap:
                self.finished.append(t)
            else:
                alive.append(t)
        self.tracks = alive
        return matched

    def count_people(self, min_hits=3):
        """영상 전체에서 착용/미착용 사람 수를 센다. (너무 짧게 잡힌 잡음은 뺀다)"""
        helmet = head = 0
        for t in self.finished + self.tracks:
            if t.hits < min_hits:
                continue
            if t.votes["head"] > t.votes["helmet"]:
                head += 1
            else:
                helmet += 1
        return helmet, head


# =============================================================================
# 2) 모델 불러오기
# =============================================================================
DEVICE = "cpu"
_model_cache = {}


def load_model(path):
    """모델을 불러온다. 같은 파일이면 다시 읽지 않고, 파일이 바뀌었으면(새 best.pt 로 교체) 다시 읽는다."""
    from ultralytics import YOLO
    p = Path(path)
    if not p.exists():
        raise FileNotFoundError(f"모델 파일이 없습니다: {path}")
    key = (str(p), p.stat().st_mtime)             # 파일 경로 + 마지막 수정 시각
    if key not in _model_cache:
        _model_cache.clear()                       # 이전 모델은 버린다
        _model_cache[key] = YOLO(str(p))
    return _model_cache[key]


# =============================================================================
# 3) 영상 한 개를 탐지한다
# =============================================================================
def draw_boxes(frame, dets):
    """탐지된 박스와 이름(helmet 0.94 같은 글)을 그린다."""
    for d in dets:
        x1, y1, x2, y2 = [int(v) for v in d["box"]]
        color = COLOR_HEAD if d["cls"] == "head" else COLOR_HELMET
        cv2.rectangle(frame, (x1, y1), (x2, y2), color, 2)
        label = f'{d["cls"]} {d["conf"]:.2f}'
        (tw, th), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.5, 1)
        cv2.rectangle(frame, (x1, y1 - th - 8), (x1 + tw + 6, y1), color, -1)
        cv2.putText(frame, label, (x1 + 3, y1 - 5), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1, cv2.LINE_AA)
    return frame


def save_png(img, path):
    """한글 경로에서도 저장되도록 imencode + tofile 을 쓴다."""
    ok, buf = cv2.imencode(".png", img)
    if ok:
        buf.tofile(str(path))


def open_camera(index):
    """웹캠을 연다. 열기 방식(DirectShow / MSMF / 자동)에 따라 화면이 검게 나오는 PC가 있어서,
    화면이 제대로 나오는(어둡지 않은) 방식을 고른다.
    모두 어두우면 가장 밝은 쪽을 쓰고 dark=True 를 돌려준다. 열 수 없으면 (None, False)."""
    best = None                                             # (밝기, 웹캠)
    for backend in (cv2.CAP_DSHOW, cv2.CAP_MSMF, cv2.CAP_ANY):
        cap = cv2.VideoCapture(index, backend)
        if not cap.isOpened():
            cap.release()
            continue
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
        mean = 0.0
        for _ in range(15):                                 # 처음 몇 장은 버린다 (밝기가 잡히는 시간)
            ok, f = cap.read()
            if ok:
                mean = float(f.mean())                      # 화면 전체의 평균 밝기 (0~255)
        if mean >= 20:                                      # 충분히 밝으면 이 방식으로 결정
            if best:
                best[1].release()
            return cap, False
        if best is None or mean > best[0]:
            if best:
                best[1].release()
            best = (mean, cap)
        else:
            cap.release()
    if best is None:
        return None, False
    return best[1], True


def run_video(cmd, cmd_queue, emit):
    video = cmd["video"]
    conf = float(cmd.get("conf", 0.5))              # 신뢰도 기준
    seconds = float(cmd.get("seconds", 3))          # 미착용이 이 시간 이어지면 알림
    stride = int(cmd.get("stride", 2))              # 2 = 프레임을 하나 건너뛰며 처리 (속도를 위해)
    imgsz = int(cmd.get("imgsz", 640))
    save_capture = bool(cmd.get("save_capture", True))
    capture_dir = Path(cmd.get("capture_dir", "captures"))

    model = load_model(cmd["model"])

    # 영상 경로가 "camera:0" 처럼 오면 웹캠(번호 0)으로 실시간 탐지한다.
    is_cam = str(video).startswith("camera:")
    if is_cam:
        cam_index = int(str(video).split(":")[1])
        cap, dark = open_camera(cam_index)
        stride = max(stride, 3)                                 # CPU 가 따라갈 수 있게 3프레임에 한 번만 처리
        if cap is None:
            emit({"type": "error", "message": f"웹캠을 열 수 없습니다 (번호 {cam_index}). 다른 프로그램이 쓰고 있는지 확인하세요."})
            return
        if dark:
            emit({"type": "warn", "message": "웹캠 화면이 매우 어둡습니다. 렌즈 덮개와 조명을 확인하세요."})
        fps, total = 30.0, 0                                    # 웹캠은 끝이 없으므로 전체 프레임 수가 없다
    else:
        cap = cv2.VideoCapture(video)
        if not cap.isOpened():
            emit({"type": "error", "message": f"영상을 열 수 없습니다: {video}"})
            return
        fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
        total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    t_start = time.perf_counter()
    read_fail = 0
    if save_capture:
        capture_dir.mkdir(parents=True, exist_ok=True)

    tracker = SimpleTracker()
    frame_no = step = alerts = 0
    ms_list = []
    stopped = False

    while True:
        # 앱이 멈추라고 했는지 확인
        try:
            c = cmd_queue.get_nowait()
            if c.get("cmd") in ("stop", "quit"):
                stopped = True
                if c.get("cmd") == "quit":
                    cmd_queue.put(c)               # quit 은 바깥 반복문이 다시 읽도록 되돌려 둔다
                break
        except queue.Empty:
            pass

        ok, frame = cap.read()
        if not ok:
            read_fail += 1
            if is_cam and read_fail < 30:           # 웹캠은 가끔 한두 번 못 읽을 수 있다
                continue
            break
        read_fail = 0
        frame_no += 1
        if (frame_no - 1) % stride != 0:
            continue
        step += 1
        # 시각(초): 영상 파일은 프레임 번호로 계산하고, 웹캠은 실제 흐른 시간으로 잰다
        t_video = (time.perf_counter() - t_start) if is_cam else frame_no / fps

        # --- 탐지 ---
        t0 = time.perf_counter()
        res = model.predict(frame, conf=conf, imgsz=imgsz, device=DEVICE, verbose=False)[0]
        names = res.names
        dets = []
        for xyxy, cf, cl in zip(res.boxes.xyxy.cpu().numpy(), res.boxes.conf.cpu().numpy(), res.boxes.cls.cpu().numpy()):
            dets.append({"box": [float(v) for v in xyxy], "conf": float(cf), "cls": names[int(cl)]})

        # --- 추적 + 알림 판단 ---
        new_alerts = []
        warn_people = 0
        for t in tracker.update(dets, step):
            if t.cls == "head":
                t.helmet_run = 0
                if t.head_since is None:
                    t.head_since = t_video
                held = t_video - t.head_since        # 미착용이 이어진 시간(초)
                if held >= seconds:
                    warn_people += 1
                    if not t.alerted:                # 같은 사람은 한 번만 알림
                        t.alerted = True
                        new_alerts.append((t, held))
            else:
                t.helmet_run += 1
                if t.helmet_run >= 5:                # 착용이 5번 연속 보이면 미착용 구간 끝
                    t.head_since = None
                    t.alerted = False

        ms = int((time.perf_counter() - t0) * 1000)
        ms_list.append(ms)

        # --- 화면 만들기 ---
        shown = draw_boxes(frame.copy(), dets)

        for t, held in new_alerts:
            alerts += 1
            now = datetime.now()
            cap_path = None
            if save_capture:
                cap_path = str(capture_dir / f"cap_{now:%H%M%S}_{frame_no}.png")
                save_png(shown, cap_path)
            emit({"type": "alert", "frame": frame_no, "conf": round(t.conf, 3), "duration": int(held),
                  "capture": cap_path, "time": now.strftime("%Y-%m-%d %H:%M:%S")})

        h, w = shown.shape[:2]
        if w > 960:                                  # 앱으로 보내는 그림이 너무 크지 않게 줄인다
            shown = cv2.resize(shown, (960, int(h * 960 / w)))
        ok2, jpg = cv2.imencode(".jpg", shown, [cv2.IMWRITE_JPEG_QUALITY, 80])
        emit({
            "type": "frame", "n": frame_no, "total": total, "ms": ms,
            "helmet": sum(1 for d in dets if d["cls"] == "helmet"),
            "head": sum(1 for d in dets if d["cls"] == "head"),
            "warn": warn_people,
            "jpg": base64.b64encode(jpg.tobytes()).decode("ascii") if ok2 else "",
        })

    cap.release()
    helmet_people, head_people = tracker.count_people()
    emit({"type": "done", "frames": step, "avg_ms": int(sum(ms_list) / len(ms_list)) if ms_list else 0,
          "helmet_people": helmet_people, "head_people": head_people, "alerts": alerts, "stopped": stopped})


# =============================================================================
# 4) 실행 방식 두 가지
# =============================================================================
def serve():
    """앱이 실행했을 때: 표준 입력으로 명령을 받으며 계속 대기한다."""
    global DEVICE
    import torch
    DEVICE = 0 if torch.cuda.is_available() else "cpu"
    import ultralytics  # noqa: F401  (미리 불러 두어야 첫 탐지가 빠르다)
    send({"type": "ready", "device": "cuda" if DEVICE == 0 else "cpu"})

    q = queue.Queue()

    def reader():                                    # 명령을 읽어 q 에 쌓는 일꾼
        for line in sys.stdin:
            line = line.strip()
            if line:
                try:
                    q.put(json.loads(line))
                except json.JSONDecodeError:
                    pass
        q.put({"cmd": "quit"})                       # 앱이 닫히면 엔진도 끝낸다

    threading.Thread(target=reader, daemon=True).start()

    while True:
        cmd = q.get()
        if cmd.get("cmd") == "quit":
            break
        if cmd.get("cmd") == "start":
            try:
                run_video(cmd, q, send)
            except Exception as e:                   # 오류가 나도 엔진은 계속 산다
                send({"type": "error", "message": f"{type(e).__name__}: {e}"})


def cli(args):
    """앱 없이 단독 시험할 때: 결과를 글자로 보여 준다."""
    global DEVICE
    import torch
    DEVICE = 0 if torch.cuda.is_available() else "cpu"
    count = {"n": 0}

    def show(m):
        if m["type"] == "frame":
            count["n"] += 1
            if count["n"] % 20 == 1:
                print(f'  프레임 {m["n"]}/{m["total"]}  {m["ms"]}ms  착용 {m["helmet"]}  미착용 {m["head"]}  경고 {m["warn"]}', file=_OUT)
        else:
            print(json.dumps(m, ensure_ascii=False), file=_OUT)

    run_video({"video": args.video, "model": args.model, "conf": args.conf, "seconds": args.seconds,
               "stride": args.stride, "capture_dir": args.capture_dir}, queue.Queue(), show)


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="SafeEye 탐지 엔진")
    ap.add_argument("--video", help="이 옵션을 주면 앱 없이 영상 하나만 탐지해 본다")
    ap.add_argument("--model", default="models/best.pt")
    ap.add_argument("--conf", type=float, default=0.5)
    ap.add_argument("--seconds", type=float, default=3)
    ap.add_argument("--stride", type=int, default=2)
    ap.add_argument("--capture-dir", default="captures")
    a = ap.parse_args()
    if a.video:
        cli(a)
    else:
        serve()
