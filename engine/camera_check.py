"""
웹캠 진단: 이 PC에서 웹캠이 몇 번으로, 어떤 방식으로 열리는지 확인한다.
(화면이나 파일에는 아무것도 저장하지 않고, 프레임이 읽히는지만 본다.)

사용법:  .venv\\Scripts\\python engine\\camera_check.py
"""
import cv2

BACKENDS = (("DirectShow", cv2.CAP_DSHOW), ("MediaFoundation", cv2.CAP_MSMF), ("자동", cv2.CAP_ANY))

found = False
for index in range(4):                       # 웹캠 번호 0~3 을 확인
    for name, backend in BACKENDS:
        cap = cv2.VideoCapture(index, backend)
        opened = cap.isOpened()
        got = False
        size = ""
        bright = 0.0
        if opened:
            for _ in range(15):                  # 처음 몇 장은 밝기가 잡히는 시간이라 버린다
                got, frame = cap.read()
            if got:
                size = f"{frame.shape[1]}x{frame.shape[0]}"
                bright = float(frame.mean())     # 화면 전체의 평균 밝기 (0=완전히 검정, 255=완전히 흰색)
        cap.release()
        if opened and got:
            found = True
            note = "  <- 너무 어둡습니다! 렌즈 덮개/조명을 확인하세요" if bright < 20 else ""
            print(f"웹캠 {index}번: 열림 ({name}, 화면 크기 {size}, 밝기 {bright:.0f}/255){note}")
            if bright >= 20:
                print(f"            -> 앱 설정의 '웹캠 번호'에 {index} 를 쓰세요")
                break
    else:
        print(f"웹캠 {index}번: 없음")

if not found:
    print("\n사용할 수 있는 웹캠이 없습니다. 확인하세요:")
    print(" 1) 웹캠이 USB에 꽂혀 있는지 (장치 관리자 > 카메라 에 보이는지)")
    print(" 2) 다른 프로그램(카메라 앱, 줌, 팀즈 등)이 웹캠을 쓰고 있지 않은지")
    print(" 3) 윈도우 설정 > 개인 정보 및 보안 > 카메라 에서 '데스크톱 앱이 카메라에 액세스하도록 허용' 이 켜져 있는지")
