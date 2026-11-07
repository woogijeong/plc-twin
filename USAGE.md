# PlcTwin 사용 방법

GX Works2 시뮬레이터(또는 PLC)의 래더 동작을 3D로 보면서 시험하는 방법입니다.
자세한 디바이스 표와 설정 키는 [README.md](README.md), 실제 PLC 시험 절차는 [HARDWARE_CHECKLIST.md](HARDWARE_CHECKLIST.md)를 보세요.

> **안전:** 앱은 **Connect를 누르기 전까지** PLC에 아무것도 보내지 않습니다. 연결하면 D0-D3, D40-D44를 매 주기 덮어쓰니 래더가 이 영역에 쓰면 안 됩니다. 실제 PLC는 실습(랩) 네트워크에서만 연결하세요.

## 1. 준비물

| 항목 | 설명 |
|---|---|
| `Build\PlcTwin.exe` | 3D 트윈 앱 |
| GX Works2 + 시뮬레이터 | 래더를 RUN 상태로 실행 |
| MX Component | GX Simulator2 연결용(기본 설치 경로 `C:\MELSEC\Act`). 앱이 도우미(`MxHelper.exe`)로 직접 사용 |
| ModbusServer (for Client) | (선택) Modbus 경로용 서버. GX-Works Version은 `GX-Works2` |
| `device_comments.xlsx` | GX Works2에 붙여 넣는 디바이스 코멘트 (2행 x 8열, 한글 2칸) |

## 2. 시뮬레이터로 시험하기

### 2.1 MX 방식 (권장, Modbus 서버 불필요)

앱이 MX Component로 GX Simulator2에 직접 연결합니다. PlcTwin이 Connect 때 32비트 도우미(`MxHelper.exe`)를 자동으로 띄우므로 별도 서버는 필요 없습니다.

1. GX Works2에서 시뮬레이션을 시작해 **RUN** 상태로 만듭니다.
2. MX Component의 Communication Setup Utility에서 **논리국번**(기본 1)이 시뮬레이터를 가리키는지 확인합니다. 실제 PLC를 가리키면 Connect 때 실제 PLC에 씁니다.
3. `Build\PlcTwin.exe`를 실행하고 전송 방식 버튼을 **MX**로 바꿉니다. IP 칸은 비활성이 되고 포트 칸이 논리국번입니다.
4. 국번을 확인하고 **Connect**를 누릅니다. 상태 줄이 `MX station 1` 연결됨으로 바뀌면 성공입니다.

설정 파일에서 기본값을 바꾸려면 `plc_twin_config.json`에 `"mx":{"logicalStation":1,"pollMs":100}`을 추가합니다(없으면 이 값이 기본). Modbus와 달리 D4-D39를 0으로 덮어쓰지 않고, 폴링도 빠릅니다.
도우미를 다시 만들려면 `tools\build_mxhelper.ps1`을 실행합니다(MX Component, .NET Framework csc, Windows SDK TlbImp 필요).

### 2.2 Modbus 방식 (ModbusServer 사용)

MC 3E는 시뮬레이터에 쓸 수 없습니다(이더넷 모듈이 없음).

1. GX Works2에서 접속 대상을 시뮬레이터로 두고 시뮬레이션을 **RUN**으로 만듭니다.
2. ModbusServer를 켜고 **PLC Connect**를 누릅니다. `D0000`, `D0100` 탭에 값이 보이는지 확인합니다.
3. 앱에서 방식을 **Modbus**, IP `127.0.0.1`, 포트 `502`로 두고 **Connect**를 누릅니다.

Modbus 한 사이클이 약 0.9초라서 반응이 느린 것은 정상입니다.

종료할 때는 앱의 Disconnect, (Modbus라면 서버의 PLC Disconnect), 시뮬레이터 정지 순서로 합니다.

시뮬레이터에서 확인된 것 (2026-11-07, 사용자 확인): Modbus 경로에서 실린더와 서보가, MX 경로에서 전체 동작이 시뮬레이터와 연동됩니다.

## 3. 화면 조작

| 위치 | 동작 |
|---|---|
| 왼쪽 위 **Metal / Nonmetal** | 매거진에 금속(파랑) / 비금속(빨강) 칩 추가 |
| **Reset** | 설비를 처음 상태로 되돌리고 칩 삭제 (연결 중에는 비활성) |
| **Default / Front / Iso / Top** | 카메라 구도 |
| **Panel** | 상태 패널 표시 토글 |
| **Connect / Disconnect** | PLC 연결 / 해제 |
| 3D 빨간 단추 | 오프라인에서 누르는 동안 ON. 연결 중에는 색만 바뀜 |
| 매거진 밖 칩 클릭 | 그 칩 삭제 |

상태 패널은 비트(초록 = ON, 주황 = OFF)와 워드 값을 보여 줍니다.

## 4. 명령 주는 법 (GX Works2 디바이스 테스트 또는 래더)

PLC가 쓰는 영역은 D100-D103, D140-D146입니다. 래더가 같은 디바이스를 쓰면 직접 쓴 값이 덮어써집니다.

### 4.1 실린더, 컨베이어

| 디바이스 | 동작 |
|---|---|
| D100.0 / D100.1 | 공급 전진 / 후진 |
| D100.2 / D100.3 | 송출 전진 / 후진 |
| D100.4 | 가공 하강 (OFF이면 상승) |
| D100.5 / D100.6 | 배출 전진 / 후진 |
| D100.7 | **스톱 실린더 상승** (OFF이면 하강 = 기본) |
| D100.8 / D100.9 | 흡착 전진 / 후진 |
| D100.A | 흡착패드(진공) On |
| D100.B / D100.C | 창고 전진 / 후진 |
| D100.D | 가공드릴 On |
| D100.E | 컨베이어 On |

실린더 위치는 앱이 D0.0-D0.E로 돌려 줍니다 (예: D100.0 ON이면 D0.0 ON).
칩이 스토퍼에서 감지되면 D1.3, 금속/비금속은 D1.2 / D1.1이 켜집니다.

### 4.2 서보

**위치 이동(Run)**

1. D102.0 ON (PLC READY) → 앱이 D2.0 ON
2. D102.1 ON (SERVO ON)
3. D102.4(STOP)가 OFF인지 확인 (ON이면 Run과 JOG가 멈춤)
4. D140 = 10 (속도, 1단위 = 5 mm/s), D142 = **9003**, D144 = 목표 위치
5. D103.0 ON → D40이 목표까지 이동, 도달하면 D3.0과 D3.4 ON
6. D103.0 OFF → D3.0, D3.4 즉시 OFF. 다음 이동은 D144를 바꾸고 다시 ON

D40 = 높이(mm) + 50, 시작값 140, 범위 약 0-150.
D142가 9003이 아니면 Run이 움직이지 않습니다.

**JOG**: D146 = 10(속도) 후 D102.8(JOG+)이면 150까지, D102.C(JOG-)이면 0까지 이동합니다.

**서보 타워 램프** (PLC 비트가 아닌 표시): Home = D40이 140, Up = D40 ≥ 150, Down = D40 ≤ 0.

## 5. 디바이스 코멘트

`device_comments.xlsx`의 A, B열(디바이스, 코멘트)을 GX Works2에 붙여 넣습니다(헤더 행 제외).
코멘트는 2행 x 8열이고 한글은 2칸이라 1행에 4글자입니다. 앱이 쓰는 영역(D0-D3, D40-D44)과 PLC가 쓰는 영역(D100-D103, D140-D146)을 E열에서 구분합니다.

## 6. 문제 해결

| 증상 | 확인할 것 |
|---|---|
| 연결이 안 됨 | MX: 국번이 맞는지, 시뮬레이터가 RUN인지, `MxHelper.exe`가 StreamingAssets에 있는지. Modbus: 서버에서 PLC Connect가 되어 있는지, 포트가 서버와 같은지(기본 502), 상태 줄의 오류 메시지 |
| 값을 써도 반응이 없음 | 서버 `D0100` 탭에 D102, D103, D140-D146 값이 들어오는지, 래더가 같은 디바이스를 덮어쓰는지 |
| 서보가 움직이지 않음 | D102.0 ON, D102.4 OFF, D142 = 9003, D144가 범위 안인지 |
| 스토퍼가 반대로 움직임 | D100.7 ON이 상승입니다. 래더 조건을 이 의미에 맞게 확인 |
| 반응이 느림 | Modbus 사이클이 약 0.9초라서 정상 (MX는 빠름) |
| 상태 줄에 `CONFIG ERROR` | `plc_twin_config.json`의 해당 값 수정 후 재시작 |

## 7. 주의

- (Modbus 모드만) 쓸 때마다 D0-D44를 한 프레임으로 보내고 **D4-D39를 0으로 씁니다.** 그 영역을 다른 용도로 쓰지 마세요.
- 서버 쓰기 표가 PLC로 미러링되는지(OQ-1)는 [HARDWARE_CHECKLIST.md](HARDWARE_CHECKLIST.md) 3.1절 시험으로 확인합니다.
- 시뮬레이터는 KeepAlive, RUN 중 쓰기 거부, 실제 end code를 흉내 내지 않습니다. 실제 PLC 시험은 체크리스트를 따르세요.
