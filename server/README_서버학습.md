# 서버에서 모델 학습하기 (WSL)

교수님 지시대로 **학습은 서버의 WSL(리눅스)에서만** 합니다. 순서대로 따라 하면 됩니다.

## 0. 준비물
- 이 `server` 폴더 전체 (train.py, setup.sh, 기본 모델 `yolov8n.pt` `yolov8s.pt` `yolov8m.pt`)
  - 기본 모델 파일은 **서버가 인터넷이 안 될 때를 대비해** 미리 넣어 둔 것입니다. 같이 옮기세요.
  - `setup.sh` 는 `pip install ultralytics` 를 하므로 서버에서 파이썬 패키지를 받을 수 있어야 합니다.
    인터넷이 막혀 있으면 교수님께 패키지 설치 방법을 문의하세요.
- Roboflow 데이터셋 zip: `Hard Hat Workers.v5-raw_headhelmetclasses.yolov8.zip`
  (내 PC에서는 이미 `data/hardhat/` 에 풀어 두었습니다. 폴더째 가져가도 됩니다.)

## 1. 파일을 서버로 옮기기
USB, 공유 폴더, 원격 데스크톱 복사 중 편한 방법을 쓰면 됩니다.

서버의 WSL에서 작업 폴더를 만들고 파일을 넣습니다.
```bash
mkdir -p ~/safeeye && cd ~/safeeye
# server 폴더 안의 파일(train.py, setup.sh)을 여기에 둔다
# 데이터셋 zip은 아래처럼 풀어서 hardhat 폴더로 만든다
unzip -q "Hard Hat Workers.v5-raw_headhelmetclasses.yolov8.zip" -d hardhat
```
완료하면 이런 모양이어야 합니다.
```
~/safeeye/
  train.py
  setup.sh
  hardhat/
    train/  valid/  test/  data.yaml
```

## 2. 환경 준비 (처음 한 번만)
```bash
bash setup.sh
```
마지막에 `GPU 사용 가능: True` 가 나오면 성공입니다. `False` 이면 GPU 드라이버 문제이니 알려 주세요.

## 3. 학습하기
먼저 **3번만 돌려서 정상 동작하는지** 확인합니다. (몇 분이면 끝남)
```bash
source venv/bin/activate
python train.py --size n --epochs 3
```
문제가 없으면 본 학습을 합니다. 작은 모델(n)과 중간 모델(s)을 둘 다 학습해서 비교하면 좋습니다.
```bash
python train.py --size n          # 약 50번 반복
python train.py --size s
```

## 4. 결과 확인
학습이 끝나면 화면 맨 아래에 이런 결과가 나옵니다.
```
===== test 성능 =====
mAP50 (전체)        : 0.xxx
head    정밀도 0.xxx  재현율 0.xxx    <- 미착용(안전모 안 씀)
helmet  정밀도 0.xxx  재현율 0.xxx    <- 착용
```
- **head 재현율**이 가장 중요합니다. (미착용자를 얼마나 놓치지 않는가)
- 이 숫자를 요구사항 분석서의 "정확도" 결과 칸에 적습니다.

## 5. 모델 파일 가져오기
결과 모델은 `output/best_n.pt`, `output/best_s.pt` 에 있습니다.
내 PC의 `C:\Users\AIOT\safety_project\models\` 폴더로 가져와서 **`best.pt` 라는 이름으로** 두면 앱이 자동으로 사용합니다.

## 문제가 생기면
| 증상 | 해결 |
|---|---|
| `데이터셋을 찾을 수 없습니다` | `hardhat/` 폴더 안에 `train/images` 가 있는지 확인 |
| GPU 사용 가능: False | `nvidia-smi` 결과를 알려 주세요 |
| 메모리 부족(CUDA out of memory) | `--batch 8` 로 줄여서 다시 실행 |
