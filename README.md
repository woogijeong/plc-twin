# PlcTwin: MPS 디지털 트윈

Mitsubishi PLC(또는 Modbus TCP 서버)와 연결해 MPS(Modular Production System) 실습 설비를 Unity 3D로 보여 주는 디지털 트윈입니다.
앱은 센서 값을 PLC에 쓰고(D0-D3, D40-D44), PLC가 쓴 명령(D100-D103, D140-D146)을 읽어 3D 설비를 움직입니다.

- 통신: MC protocol 3E binary(TCP) 또는 Modbus TCP
- 설비: 공급, 송출, 가공(드릴), 배출 실린더, 컨베이어, 스토퍼, 서보 리프터 + 흡착패드, 창고 실린더
- 모델: Blender로 만든 3D 모델(`tools\blender\build_mps.py`). 기본 룩은 스타일 A(밝은 산업 장비)

> **안전:** 앱은 처음에 **Disconnected**(오프라인)로 시작합니다. 실제 PLC에는 **Connect를 누르기 전까지** 아무것도 보내지 않습니다.
> PLC의 Write during RUN이 켜져 있으면 같은 LAN의 누구나 값을 쓸 수 있으니 **실습(랩) 네트워크에서만** 연결하세요.
> 자세한 절차는 [HARDWARE_CHECKLIST.md](HARDWARE_CHECKLIST.md)를 보세요.

---

## 1. 실행

```
Build\PlcTwin.exe
```

선택 인수 (모두 생략 가능):

| 인수 | 설명 |
|---|---|
| `-view default\|front\|iso\|top` | 시작 구도 (기본 `default`) |
| `-style a\|b\|c` | 룩 (기본 `a`). b: 실물 키트, c: 청사진(외곽선은 이전 프리미티브 모델용이라 현재 모델에서는 동작하지 않음) |

창은 크기를 바꿀 수 있고(기본 1387 x 1000), 패널과 3D 화면이 같이 줄어듭니다. 마우스로 카메라를 움직이지는 않습니다.

## 2. 화면 구성

| 위치 | 이름 | 동작 |
|---|---|---|
| 왼쪽 위 | **Metal / Nonmetal** | 매거진에 금속(파랑) / 비금속(빨강) 칩 추가 |
| 그 아래 | **Reset** | 설비를 전원 켠 상태로 되돌리고 칩을 모두 지움. **연결 중에는 비활성** |
| 위쪽 | 상태 패널 | 비트 45칸(D0-D3, D100-D103) + 워드 9칸. 초록 = ON, 주황 = OFF |
| 아래 | **Connect / Disconnect** | PLC 연결 / 해제 |
| 아래 | **Panel** | 상태 패널 표시 토글 (표시만 바뀜) |
| 아래 | **Default / Front / Iso / Top** | 카메라 구도 4가지 (0.4초 전환) |
| 아래 | MC 3E / Modbus, IP, 포트 | 통신 방식과 대상 (연결 전에만 수정 가능, 이번 실행에서만 유효하고 파일에 저장하지 않음) |
| 맨 아래 줄 | 상태 | `Disconnected`, 연결 상태, 오류 메시지 |

3D 장비의 빨간 단추는 오프라인에서 누르는 동안(좌클릭 유지) ON입니다. 연결 중에는 PLC가 설비를 제어하고 단추는 색만 바뀝니다.
매거진 밖의 칩을 클릭하면 그 칩이 지워집니다.

### 서보 타워 램프

리프트 기둥의 램프 3개는 서보 높이를 표시합니다. PLC 비트는 아닙니다.

| 램프 | 켜지는 조건 |
|---|---|
| Home | D40 = 140 (시작 위치) |
| Up | 위 한계 이상 (D40 >= 150) |
| Down | 아래 한계 이하 (D40 <= 0) |

## 3. 디바이스 맵 (모두 D 디바이스, 10진수)

앱이 **쓰는** 영역 (PLC 래더는 쓰면 안 됩니다):

| 디바이스 | 내용 |
|---|---|
| D0.0-D0.B, D0.D-D0.E | 실린더 위치 (공급, 송출, 가공 하강·상승, 배출, 스토퍼 하강·상승, 흡착 전진·후진, 창고 전진·후진) |
| D0.C | 흡착패드 On |
| D0.F | 공급센서 감지 |
| D1.1 / D1.2 | 물품감지 비금속 / 금속 |
| D1.3 | 물품감지 스토퍼 |
| D2.0 / D2.8 / D2.C | DRIVER READY / 1축 ERROR(항상 0) / 1축 BUSY |
| D3.0 / D3.4 | 1축 기동완료 / 위치완료 |
| D40 | 서보 현재 위치 (단위 mm, 오프셋 50: D40 = 높이(mm) + 50, 시작 140, 범위 약 0-150) |
| D42-D44 | 에러번호, 입력신호, 현재상태 (항상 0) |

앱이 **읽는** 영역 (PLC 래더가 씀):

| 디바이스 | 내용 |
|---|---|
| D100.0-D100.4 | 공급 전진·후진, 송출 전진·후진, 가공 하강 |
| D100.5-D100.7 | 배출 전진·후진, **스톱실린더 상승** (D100.7 ON이면 스토퍼가 올라가 칩을 막고, OFF이면 하강 = 기본 상태) |
| D100.8-D100.A | 흡착 전진·후진, 흡착패드(진공) On |
| D100.B-D100.C | 창고 전진·후진 |
| D100.D / D100.E | 가공드릴 On / 컨베이어 On |
| D102.0 / D102.1 / D102.4 | PLC READY / SERVO ON / 1축 STOP |
| D102.8 / D102.C | 1축 JOG+ / JOG- |
| D103.0 | 1축 위치기동(Run) |
| D140 | 전송속도 (Run 속도, 1 단위 = 5 mm/s) |
| D142 | 위치기동 명령 코드. **9003일 때만** Run 유효 |
| D144 | 목표 위치 (D40과 같은 단위) |
| D146 | JOG 속도 (1 단위 = 5 mm/s) |

시작 시 D1.0(송출센서 감지)은 더 이상 쓰지 않고 항상 0입니다.

기본 맵은 `plc_twin_config.json`의 `map`에서 바꿀 수 있습니다 (블록 시작 번호, 겹치면 안 됨).

## 4. 동작 규칙 요약

- **오프라인(Disconnected, Connecting, Error)**: 서보 정지, 진공 꺼짐, 3D 단추 사용 가능. 마지막 PLC 값은 패널 표시용으로만 유지합니다.
- **연결(Connected)**: PLC 명령만 설비를 움직입니다.
- 서보는 Run 때 목표에 정확히 멈춥니다(D3.0/D3.4가 항상 도달 가능). JOG는 한쪽 방향 한계(D40 0 ~ 150)이고, Run은 소프트 리밋이 없습니다.
- STOP(D102.4)이 ON이면 JOG와 Run이 멈춥니다(참조 앱과 다른 점). 에러 클리어(D143), ERROR, 에러번호는 동작이 없습니다.
- 컨베이어의 모든 칩이 움직이고, 멈춘 칩 뒤에 40 mm 간격으로 줄을 섭니다.
- 앱을 끌 때 D0-D3/D40-D44는 마지막으로 쓴 값 그대로 남습니다.
- 알려진 한계: D3.0의 하강 에지가 D40보다 한 번 늦게 나옵니다(MC 0x1402 랜덤 쓰기 등으로 개선 가능).

## 5. 설정 파일

`Build\PlcTwin_Data\StreamingAssets\plc_twin_config.json` (개발 중에는 `PlcTwin\Assets\StreamingAssets\`). 텍스트 편집기로 고치고 앱을 다시 시작하면 반영됩니다. 잘못된 값이면 상태 줄에 `CONFIG ERROR`가 나옵니다.

| 키 | 기본값 | 설명 |
|---|---|---|
| `transport` | `mc3e` | `mc3e` 또는 `modbus` |
| `mc3e.ip` / `port` | `192.168.3.39` / `5010` | 숫자 IPv4만 가능. 포트는 PLC open setting과 같아야 함 |
| `mc3e.monitorTimer250ms` | `4` | PLC monitoring timer (250 ms 단위, 1 이상) |
| `mc3e.pollMs` | `100` | 폴링 주기 (10 이상) |
| `modbus.ip` / `port` / `unitId` | `127.0.0.1` / `502` / `1` | |
| `modbus.pollMs` | `200` | 폴링 주기 (10 이상, 필수 키) |
| `modbus.writeTableBase` / `readTableBase` | `0` / `100` | 서버의 쓰기 / 읽기 표의 첫 D 번호 (각 80워드) |
| `connectTimeoutMs`, `timeoutMs` | `3000`, `1500` | `timeoutMs >= monitorTimer250ms*250+500` |
| `map` | `D0`, `D40`, `D100`, `D140` | `inBits`(4워드), `inWords`(5), `outBits`(4), `outWords`(7) |
| `panelVisible` | `true` | 시작할 때 상태 패널 표시 |

예전 설정(`conveyor`/`tower`/`sensor` 구역)은 더 이상 지원하지 않고 오류로 거부합니다.

### Modbus 서버 참고

- 서버는 쓰기(FC16)의 시작 주소를 무시합니다. 그래서 Modbus 모드에서는 쓸 때마다 D0부터 D44까지 한 프레임으로 보내고 D4-D39는 0으로 씁니다. **Modbus 모드에서 D4-D39를 다른 용도로 쓰지 마세요.** (mc3e는 D0-D3과 D40-D44만 씁니다.)
- 서버의 쓰기 표가 실제 PLC로 미러링되는지는 아직 확인하지 못했습니다. [HARDWARE_CHECKLIST.md](HARDWARE_CHECKLIST.md)의 OQ-1 시험을 하세요.
- 서버는 한 클라이언트만 받습니다. 요청당 약 310 ms라서 Modbus 한 사이클(쓰기 2 + 읽기 1)이 약 0.9초 걸립니다.

## 6. 개발

프로젝트: `PlcTwin\` (Unity 6000.5.10f1). 소스는 `PlcTwin\Assets\PlcTwin\`:

| 폴더 | 내용 |
|---|---|
| `Sim\` | 설비 모델(`MpsModel`), 기하(`MpsGeometry`), D 맵(`PlcMap`), 패널 배치(`PanelLayout`). Unity 의존 없음 |
| `Mc\` | MC 3E / Modbus 프레임, 전송, `PlcPoller` |
| `Twin\` | 앱(`PlcTwinApp`), 3D 연결(`MpsView`), 패널(`StatusPanel`), 룩(`TwinStyle`) |
| `Editor\` | `SceneBuilder`(씬/플레이어 빌드), Fake PLC 서버와 창 |
| `Tests\EditMode\` | 단위·통합 테스트 |

명령 (저장소 루트에서):

```powershell
# 테스트 (성공하면 출력 없음, 요약은 tools\out\summary.txt)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\run_tests.ps1

# 플레이어 빌드 (씬 + Build\PlcTwin.exe). Unity 에디터를 닫은 상태에서
"C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe" -batchmode -quit -projectPath PlcTwin -executeMethod PlcTwin.EditorTools.SceneBuilder.BuildPlayer

# 화면 캡처 / 구도 비교
powershell -NoProfile -ExecutionPolicy Bypass -File tools\shots.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\view_preview.ps1
```

### 3D 모델 바꾸기

Blender 5.1로 `tools\blender\build_mps.py`를 돌리면 `tools\blender\out\mps.fbx`(메시)와 `layout.json`(노드 이름과 위치)이 나옵니다.
`SceneBuilder`가 이 둘로 씬을 만듭니다. 새 이름을 `MpsView`가 찾으므로 바인딩 이름 48개(`MpsView.BoundNames`)는 바꾸지 마세요(`SceneBindingTests`가 확인).

```
blender.exe -b --factory-startup -P tools\blender\build_mps.py -- --no-render
```

### PLC 없이 시험

Unity 에디터에서 `Assets/Scenes/Main.unity`를 열고 **Play를 먼저** 누릅니다(Play 진입 때 도메인이 다시 로드되어 그 전에 켠 가짜 서버는 꺼집니다). 그다음 메뉴 **PlcTwin > Fake PLC** 창에서 Start를 누릅니다. 가짜 MC/Modbus 서버가 `127.0.0.1`에서 열립니다. 앱에서 IP를 `127.0.0.1`로 두고 Connect하세요.
Fake 서버는 프레이밍과 end code, 무응답은 흉내 내지만 PLC 쪽 동작(KeepAlive, RUN 중 쓰기 거부, 래더 스캔 덮어쓰기)은 흉내 내지 않습니다.

## 7. 문서

| 파일 | 내용 |
|---|---|
| [HARDWARE_CHECKLIST.md](HARDWARE_CHECKLIST.md) | 실제 PLC / Modbus 서버와 연결해 확인하는 절차 |
| [ac3_scenario.md](ac3_scenario.md) | 실제 래더 시나리오 기록표 |
| `.omc\specs\mps-reference-behavior.md` | 참조 앱 동작 분석 |
| `.omc\plans\ralplan-mps-twin.md`, `open-questions-mps.md` | 계획과 질문/답변 |
