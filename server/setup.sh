#!/bin/bash
# 서버(WSL)에서 한 번만 실행하는 환경 준비 스크립트
# 사용법:  bash setup.sh

set -e

# 1) 이 프로젝트 전용 파이썬 환경(venv)을 만든다
python3 -m venv venv
source venv/bin/activate

# 2) YOLOv8(ultralytics)을 설치한다 (PyTorch도 함께 설치됨)
pip install --upgrade pip
pip install ultralytics

# 3) GPU가 잡히는지 확인한다 (True 가 나오면 GPU로 학습 가능)
python -c "import torch; print('GPU 사용 가능:', torch.cuda.is_available())"
nvidia-smi || echo "nvidia-smi 가 실행되지 않습니다. GPU 드라이버를 확인하세요."

echo "준비 완료. 다음 명령으로 학습하세요:  source venv/bin/activate && python train.py --size n"
