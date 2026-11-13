"""
서버(WSL)에서 안전모 탐지 모델(YOLOv8)을 학습하는 스크립트

사용법 (서버의 WSL에서):
    python train.py --size n          # 가장 작은 모델 (빠름)
    python train.py --size s          # 조금 더 큰 모델
    python train.py --size m          # 더 큰 모델 (느리지만 더 정확)

하는 일:
    1. 데이터셋 폴더(hardhat/)를 찾아 학습용 data 설정 파일을 만든다.
    2. YOLOv8 모델을 학습한다.
    3. 학습에 쓰지 않은 test 사진으로 성능(mAP, 클래스별 재현율)을 잰다.
    4. 결과 모델을 output/best_<크기>.pt 로 복사한다. -> 이 파일을 내 PC의 models/best.pt 로 가져간다.
"""
import argparse
import shutil
from pathlib import Path

from ultralytics import YOLO

HERE = Path(__file__).resolve().parent


def make_data_yaml(data_dir: Path) -> Path:
    """Roboflow가 만든 data.yaml은 경로가 '../train/images'처럼 상대경로라서,
    실제 폴더 위치(절대경로)를 적은 새 설정 파일을 만든다."""
    text = (
        f"path: {data_dir.as_posix()}\n"
        "train: train/images\n"
        "val: valid/images\n"
        "test: test/images\n"
        "nc: 2\n"
        "names: ['head', 'helmet']\n"   # head = 안전모 미착용, helmet = 안전모 착용
    )
    out = HERE / "data_server.yaml"
    out.write_text(text, encoding="utf-8")
    return out


def main():
    ap = argparse.ArgumentParser(description="안전모 탐지 모델 학습")
    ap.add_argument("--size", default="n", choices=["n", "s", "m"], help="모델 크기 (n=작음, s=중간, m=큼)")
    ap.add_argument("--epochs", type=int, default=50, help="학습 반복 횟수")
    ap.add_argument("--imgsz", type=int, default=640, help="학습 이미지 크기")
    ap.add_argument("--batch", type=int, default=16, help="한 번에 학습할 사진 수")
    ap.add_argument("--device", default=None, help="사용할 장치. 비우면 자동 (GPU가 있으면 GPU)")
    ap.add_argument("--workers", type=int, default=8, help="데이터 읽기 작업자 수")
    ap.add_argument("--fraction", type=float, default=1.0, help="학습 사진 사용 비율 (시험용으로 줄일 때만 사용)")
    ap.add_argument("--data-dir", default=str(HERE / "hardhat"), help="압축 푼 데이터셋 폴더")
    args = ap.parse_args()

    data_dir = Path(args.data_dir).resolve()
    if not (data_dir / "train" / "images").exists():
        raise SystemExit(f"데이터셋을 찾을 수 없습니다: {data_dir}\n"
                         "Roboflow zip을 풀어서 hardhat/ 폴더에 넣었는지 확인하세요.")

    data_yaml = make_data_yaml(data_dir)
    name = f"hardhat_{args.size}"

    # 1) 학습 -------------------------------------------------------------
    model = YOLO(f"yolov8{args.size}.pt")   # 처음 실행하면 기본 모델을 자동으로 내려받는다
    model.train(
        data=str(data_yaml),
        epochs=args.epochs,
        imgsz=args.imgsz,
        batch=args.batch,
        device=args.device,
        workers=args.workers,
        fraction=args.fraction,
        project=str(HERE / "runs"),
        name=name,
        exist_ok=True,
        patience=15,          # 15번 연속 좋아지지 않으면 일찍 멈춘다
    )

    # 2) test 사진으로 성능 측정 -------------------------------------------
    best = HERE / "runs" / name / "weights" / "best.pt"
    trained = YOLO(str(best))
    m = trained.val(data=str(data_yaml), split="test", imgsz=args.imgsz, device=args.device)

    print("\n===== test 성능 =====")
    print(f"mAP50 (전체)        : {m.box.map50:.3f}")
    print(f"mAP50-95 (전체)     : {m.box.map:.3f}")
    for i, cname in enumerate(m.names.values()):
        # m.box.p / m.box.r 는 클래스별 정밀도 / 재현율
        print(f"{cname:7s} 정밀도 {m.box.p[i]:.3f}  재현율 {m.box.r[i]:.3f}")
    print("(head = 안전모 미착용: 재현율이 높을수록 미착용자를 덜 놓친다)")

    # 3) 가져가기 쉽게 복사 --------------------------------------------------
    out_dir = HERE / "output"
    out_dir.mkdir(exist_ok=True)
    out_file = out_dir / f"best_{args.size}.pt"
    shutil.copy2(best, out_file)
    print(f"\n완료! 이 파일을 내 PC의 models/best.pt 로 가져가세요: {out_file}")


if __name__ == "__main__":
    main()
