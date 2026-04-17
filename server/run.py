import subprocess
import time

while True:
    print("서버 시작...")
    subprocess.run(["uvicorn", "main:app", "--host", "0.0.0.0", "--reload"])
    print("서버가 종료됐어요. 3초 후 재시작...")
    time.sleep(3)
