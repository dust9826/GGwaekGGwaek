using System;

namespace PPack
{
    /// <summary>
    /// 제설날이 지고 다니는 눈의 <b>원장</b>과, 그 원장을 필드에 놓고 되찾는 <b>영수증</b>.
    /// 순수 C# 이고 <c>UnityEngine</c> 을 참조하지 않는다 — 데디 서버에서 그대로 돈다.
    ///
    /// 화폐 단위는 <b>cm·셀</b>(<c>ccell</c>) 정수다. 셀 하나의 깊이 1cm 가 1 ccell 이고,
    /// 12.5cm 셀에서 그것은 <c>0.125² × 0.01 m³ = 0.15625 L</c> 다. <b>질량 경로에 부동소수가
    /// 하나도 없다</b> — 필드는 셀당 1바이트 정수, 합은 <c>long</c>, 원장도 <c>long</c> 이라
    /// 불변식이 근사가 아니라 <b>항등</b>이다.
    ///
    /// <b>영수증</b>
    /// ----------
    /// 방출은 셀마다 <b>실제로 쓴 정수 cm</b> 를 <see cref="_receiptCm"/> 에 적어 둔다. 다음 스텝의
    /// 지우기는 그 정수를 읽어 <b>정확히 그만큼</b> 뺀다 — 지난 프레임의 포즈·높이·정상 길이로
    /// 발자국을 다시 유도하지 않는다. v7 이 이 설계를 고른 이유가 "다시 유도한 값 중 하나라도
    /// 어긋나면 영수증이 미결로 남고, 필드가 그 질량을 갖고 있는 동안 원장도 그것을 세고 있다" 였다.
    ///
    /// <b>왜 지우기가 영수증보다 적게 가져갈 수 있는가</b>
    /// <see cref="ErasePaidReceipt"/> 는 <c>min(영수증, 지금 그 셀의 깊이)</c> 를 가져간다.
    /// relax 가 한 프레임 동안 힙 재료를 이웃으로 옮겼으면 셀은 영수증보다 적게 갖고 있다. 그
    /// 차이는 <b>파괴된 것이 아니라 이웃에 있고</b>, 필드 합이 그것을 원래 크기로 세고 있으므로
    /// 불변식은 움직이지 않는다.
    ///
    /// <b>반올림은 버리지 않고 장부에 남긴다</b>
    /// 목표 높이는 실수이고 필드는 정수 cm 다. 그 사이의 반올림을 셀마다 버리면 한 방향으로
    /// 치우친 편향이 되고, 그것이 v7 에서 -1.40 mL/step 의 단조 누출을 만든 바로 그 결함이다.
    /// 여기서는 <see cref="PlaceScanned"/> 가 <b>누적 목표에서 실제 배치량을 뺀 값</b>을 매 셀의
    /// 몫으로 쓴다(디지털 미분 해석기와 같은 형태). 그래서
    /// <list type="number">
    /// <item>마지막 셀의 누적 목표가 정확히 배치 총량과 같아 <b>총합이 정확</b>하고,</item>
    /// <item>어떤 셀이 천장에 막혀 덜 받으면 그 부족분이 <b>다음 셀로 넘어가</b> 자동 재시도되고,</item>
    /// <item>어디에서도 반올림 잔여가 <b>사라지지 않는다</b> — 아무 셀도 못 받은 양은 원장에
    ///   그대로 남아 불변식이 <c>carried</c> 로 센다.</item>
    /// </list>
    /// </summary>
    public sealed class SnowPlowLedger
    {
        // 가중치 고정소수 스케일(1m 당). 모양만 정하고 질량은 정하지 않으므로 정밀도가 자유롭다 —
        // 반대로 말하면 이 값이 틀려도 질량은 안 틀린다. 그것이 배분기를 정수로 짠 이유다.
        private const int WeightPerMetre = 65536;

        private readonly SnowField _field;

        // 셀당 마지막 방출이 실제로 쓴 cm. 이것이 영수증이다.
        private readonly int[] _receiptCm;

        // 스캔 스크래치. 방출은 2패스다(가중치 스캔 → 정수 배분) — 두 패스가 <b>같은 집합</b>을
        // 돌아야 하므로 첫 패스가 집합을 기록하고 둘째 패스가 그것을 다시 훑는다. v7 이 "정규화
        // 분모와 쓰기 집합이 같은 판정이어야 한다"고 못 박은 자리이고, unplaced 가 0 인 근거다.
        private int[] _scanCell = new int[4096];
        private int[] _scanWeight = new int[4096];
        private int _scanCount;
        private long _scanWeightSum;

        private long _carriedCm;      // 날이 지고 있는 원장
        private long _bermCm;         // 소반 옆에 놓기로 예약된 양. 아직 필드에 없으므로 carried 다
        private long _deletedCm;      // 의도적으로 파괴한 것을 <b>장부에 적은</b> 총량
        private long _initialCm;      // 리셋 시점의 필드 합
        private long _unplacedCm;     // 이 스텝에 아무 셀도 못 받은 양. 영원히 0 이어야 한다
        private long _unplacedPeakCm;

        private int _receiptMinX, _receiptMinY, _receiptMaxX, _receiptMaxY;
        private long _receiptSumCm;

        private readonly float _litresPerCm;
        private readonly float _cubicMetresPerCm;

        public SnowPlowLedger(SnowField field)
        {
            _field = field ?? throw new ArgumentNullException(nameof(field));
            _receiptCm = new int[field.Width * field.Height];

            float cellArea = field.CellSize * field.CellSize;
            _cubicMetresPerCm = cellArea * 0.01f;
            _litresPerCm = _cubicMetresPerCm * 1000f;

            Reset();
        }

        /// <summary>ccell 하나의 부피(L). 12.5cm 셀에서 0.15625 — <b>이 시스템의 양자</b>다.</summary>
        public float LitresPerCm => _litresPerCm;

        /// <summary>날이 지고 있는 양(L).</summary>
        public float CarriedLitres => _carriedCm * _litresPerCm;

        /// <summary>영수증이 걸린 힙의 부피(m³). <b>필드에 실제로 쓴 것의 합</b>이지 예측이 아니다.</summary>
        public float PileVolumeM3 => _receiptSumCm * _cubicMetresPerCm;

        /// <summary>장부에 적은 파괴 총량(L).</summary>
        public float DeletedLitres => _deletedCm * _litresPerCm;

        /// <summary>리셋 시점의 필드 부피(L). 비례 톨러런스의 기준이다.</summary>
        public float InitialLitres => _initialCm * _litresPerCm;

        /// <summary>
        /// <c>field + carried + deleted - initial</c>, ccell 정수. <b>항등으로 0 이다.</b>
        ///
        /// 모든 이동이 정수 짝이기 때문이다: 자를 때 필드에서 빠진 정수가 <c>conserved + loss</c>
        /// 로 정확히 갈라지고, 방출은 필드에 더한 정수만큼만 원장에서 빠지고, relax 는 짝마다
        /// 같은 정수를 한쪽에서 빼 다른 쪽에 더한다. 0 이 아니면 <b>산술 잡음이 아니라 결함</b>이다.
        /// </summary>
        public long InvariantCm => _field.TotalDepthCm + _carriedCm + _bermCm + _deletedCm - _initialCm;

        public float InvariantErrorL => InvariantCm * _litresPerCm;

        /// <summary>
        /// 이 스텝에 <b>어느 셀도 받지 못한</b> 양(L). <b>영원히 0 이어야 한다.</b>
        ///
        /// 왜 불변식으로 충분하지 않은가: 불변식은 장부에 안 적힌 이동을 잡지만 톨러런스를 넘길
        /// 만큼 쌓인 뒤에야 잡는다(v7 에서 소반 누출은 46 mL/s 로 3분이 걸렸다). 이것은 적분이
        /// 아니라 <b>결함 자체</b>를 읽는다 — 스캔의 분모에 들어간 셀은 정의상 쓸 수 있는 셀이므로,
        /// 분모와 쓰기 집합이 갈라지는 변경이 생기는 즉시 <b>전체 크기로</b> 보고된다.
        ///
        /// 0 이 아닌 정상적인 경우는 하나뿐이다: 발자국 전체가 <see cref="SnowField.MaxDepthCm"/>
        /// 천장에 닿아 더 쌓을 자리가 없을 때. 그때는 스테이지의 최대 깊이가 힙 높이에 비해
        /// 너무 낮다는 뜻이고, 그것도 읽어야 하는 사실이다.
        /// </summary>
        public float UnplacedLitres => _unplacedCm * _litresPerCm;

        /// <summary>리셋 이후 <see cref="UnplacedLitres"/> 의 최댓값. 한 프레임 스파이크가 놓치지 않게.</summary>
        public float UnplacedPeakLitres => _unplacedPeakCm * _litresPerCm;

        /// <summary>영수증이 걸린 셀의 바운딩 박스. relax 의 가드와 지우기가 이것만 돈다.</summary>
        public bool HasReceipt => _receiptMaxX >= _receiptMinX;
        public int ReceiptMinX => _receiptMinX;
        public int ReceiptMinY => _receiptMinY;
        public int ReceiptMaxX => _receiptMaxX;
        public int ReceiptMaxY => _receiptMaxY;

        /// <summary>셀의 영수증(cm). relax 의 가드가 "이 짝이 힙 안인가"를 이걸로 판정한다.</summary>
        public int ReceiptCmAtCell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _field.Width || y >= _field.Height) return 0;
            return _receiptCm[y * _field.Width + x];
        }

        public void Reset()
        {
            Array.Clear(_receiptCm, 0, _receiptCm.Length);
            _carriedCm = 0;
            _bermCm = 0;
            _deletedCm = 0;
            _unplacedCm = 0;
            _unplacedPeakCm = 0;
            _receiptSumCm = 0;
            ClearReceiptRect();

            // 리셋은 필드를 만든 <b>뒤에</b> 불러야 한다. 여기서 잡는 값이 불변식의 기준이다.
            _initialCm = _field.TotalDepthCm;
        }

        /// <summary>스텝의 첫 줄. <see cref="UnplacedLitres"/> 는 누적이 아니라 <b>수위</b>다.</summary>
        public void BeginStep() => _unplacedCm = 0;

        // ------------------------------------------------------------------ 자르기의 장부

        /// <summary>
        /// 지형에서 실제로 빠진 <paramref name="removedCm"/> 를 갈라 장부에 적는다.
        ///
        /// <paramref name="conservedPermille"/> 만큼이 원장에 닿고, 나머지가 <b>의도적 손실</b>이다.
        /// 손실 중 <paramref name="bermPermille"/> 는 소반으로 놓이고 나머지는 파괴로 적힌다.
        ///
        /// <b>정수 나눗셈의 나머지는 손실 쪽으로 흐른다.</b> 버리지 않는다 — 세 항의 합이 항상
        /// <paramref name="removedCm"/> 와 정확히 같아야 하고, 그것이 이 함수의 전부다.
        /// </summary>
        public void CreditCut(long removedCm, int conservedPermille, int bermPermille)
        {
            if (removedCm <= 0) return;

            long conserved = removedCm * conservedPermille / 1000;
            long loss = removedCm - conserved;              // 나머지가 여기로 들어간다
            long berm = loss * bermPermille / 1000;

            _carriedCm += conserved;
            _bermCm += berm;
            _deletedCm += loss - berm;                      // 여기도 나머지가 흡수된다
        }

        /// <summary>순수 파괴. 지수 감쇠 같은 A/B 채널이 쓴다.</summary>
        public long DeleteCarried(long amountCm)
        {
            if (amountCm <= 0) return 0;
            if (amountCm > _carriedCm) amountCm = _carriedCm;

            _carriedCm -= amountCm;
            _deletedCm += amountCm;
            return amountCm;
        }

        public long CarriedCm => _carriedCm;
        public long BermCm => _bermCm;

        // ------------------------------------------------------------------ 영수증

        /// <summary>
        /// 영수증을 읽어 필드에서 <b>정확히 그만큼</b> 빼고 원장으로 되돌린다. 힙을 새 포즈에
        /// 다시 놓기 위한 준비다.
        ///
        /// 매 스텝 <b>무조건</b> 불러야 한다. 조용한 프레임에 건너뛰면 영수증이 미결로 남고,
        /// 필드가 그 질량을 갖고 있는 동안 원장도 그것을 세고 있다 — 이 설계가 LEAK 를 찍을 수
        /// 있는 유일한 경로다.
        /// </summary>
        /// <returns>실제로 되찾은 양(cm).</returns>
        public long ErasePaidReceipt()
        {
            if (!HasReceipt) return 0;

            long back = 0;
            int width = _field.Width;

            for (int y = _receiptMinY; y <= _receiptMaxY; y++)
            {
                int row = y * width;
                for (int x = _receiptMinX; x <= _receiptMaxX; x++)
                {
                    int i = row + x;
                    int r = _receiptCm[i];
                    if (r <= 0) continue;

                    // min(영수증, 실제 깊이)이 된다 — ApplyCellDelta 가 0 에서 클램프하고
                    // 삼킨 양을 돌려주므로, 부르는 쪽이 산술을 따로 하지 않는다.
                    back += -_field.ApplyCellDelta(x, y, -r);
                    _receiptCm[i] = 0;
                }
            }

            _carriedCm += back;
            _receiptSumCm = 0;
            ClearReceiptRect();
            return back;
        }

        /// <summary>
        /// 영수증을 <b>퇴역</b>시킨다 — 필드를 건드리지 않고 청구권만 없앤다. 이것이 <b>내려놓기</b>다.
        ///
        /// 날을 들었을 때·후진할 때·멈췄을 때가 모두 같은 물리적 진술이다: <b>날의 판이 이것을
        /// 더 이상 받치고 있지 않다.</b> 더미는 서 있던 자리에 영구 필드 질량으로 남고, 지고 있던
        /// 부피는 0 이 되고, <b>불변식은 1 mL 도 움직이지 않는다</b> — 필드를 건드리지 않았고
        /// 원장은 방출 때 이미 이 질량을 내보냈기 때문이다. 논증이 아니라 자명함이 요점이다.
        /// </summary>
        /// <returns>서 있는 채로 남긴 부피(m³). 계측용이고 장부에는 아무 일도 하지 않는다.</returns>
        public float RetireReceipt()
        {
            if (!HasReceipt) return 0f;

            float leftStanding = PileVolumeM3;
            int width = _field.Width;

            for (int y = _receiptMinY; y <= _receiptMaxY; y++)
            {
                int row = y * width;
                for (int x = _receiptMinX; x <= _receiptMaxX; x++) _receiptCm[row + x] = 0;
            }

            _receiptSumCm = 0;
            ClearReceiptRect();
            return leftStanding;
        }

        // ------------------------------------------------------------------ 스캔과 배분

        /// <summary>2패스 방출의 1패스를 시작한다.</summary>
        public void BeginScan()
        {
            _scanCount = 0;
            _scanWeightSum = 0;
        }

        /// <summary>
        /// 셀 하나를 분모에 넣는다. <paramref name="heightM"/> 가 0 이하면 넣지 않는다.
        ///
        /// <b>가중치를 여유 공간으로 자른다.</b> 그래야 분모에 들어간 셀이 정의상 그 몫을 받을 수
        /// 있고, <see cref="UnplacedLitres"/> 가 항등으로 0 이 된다. 천장이 무는 자리에서 모양이
        /// 낮아지고 넓어지는 것은 결함이 아니라 <b>1바이트 격자의 정직한 결과</b>다.
        /// </summary>
        public void ScanCell(int x, int y, float heightM)
        {
            if (heightM <= 0f) return;

            int headroom = _field.HeadroomCmAtCell(x, y);
            if (headroom <= 0) return;

            int w = (int)(heightM * WeightPerMetre);
            if (w <= 0) return;

            int cap = headroom * WeightPerMetre / 100;       // cm → 가중치 단위. 곱을 먼저 한다
            if (w > cap) w = cap;

            if (_scanCount == _scanCell.Length)
            {
                Array.Resize(ref _scanCell, _scanCount * 2);
                Array.Resize(ref _scanWeight, _scanCount * 2);
            }

            _scanCell[_scanCount] = y * _field.Width + x;
            _scanWeight[_scanCount] = w;
            _scanCount++;
            _scanWeightSum += w;
        }

        /// <summary>
        /// 스캔한 집합이 <b>거절 없이</b> 받을 수 있는 총량(cm). 배치 상한이고,
        /// <see cref="UnplacedLitres"/> 가 0 인 것을 <b>희망이 아니라 증명</b>으로 만드는 값이다.
        ///
        /// 근거: <see cref="ScanCell"/> 이 셀의 가중치를 <c>min(프로파일, 여유)</c> 로 자르므로
        /// 가중치 합을 cm 로 환산한 이 값이 곧 <c>Σ min(프로파일, 여유)</c> 다. 배분은 가중치에
        /// 비례하므로 총량이 이 값이면 셀 <c>i</c> 의 몫은
        /// <c>이값 · wᵢ / Σw = wᵢ/scale = min(프로파일ᵢ, 여유ᵢ) ≤ 여유ᵢ</c> 로 <b>정확히 묶인다.</b>
        ///
        /// ⚠ 여유 합(<c>Σ 여유ᵢ</c>)을 상한으로 쓰면 <b>안 된다</b> — 가중치와 여유의 비가 셀마다
        /// 다르므로 프로파일이 얕은 셀이 많으면 깊은 셀에 자기 여유보다 많은 몫이 배정된다.
        /// 처음에 그렇게 썼고, 헤드리스 하네스가 unplaced 156.7 L 로 즉시 잡아냈다 — 계기가
        /// 자기 일을 한 자리다.
        /// </summary>
        public long ScannedCapacityCm => _scanWeightSum * 100 / WeightPerMetre;

        /// <summary>스캔에 들어간 셀 수. relax 비용과 같은 자리수인지 보는 계측값이다.</summary>
        public int ScannedCells => _scanCount;

        /// <summary>
        /// 스캔한 집합에 <paramref name="amountCm"/> 를 <b>정확히</b> 배분한다.
        ///
        /// 각 셀의 몫은 <c>floor(총량 · 누적가중치 / 가중치합) - 지금까지 배치한 양</c> 이다.
        /// 마지막 셀에서 누적가중치가 가중치합과 같으므로 목표가 정확히 총량이 되고, 중간에
        /// 천장에 막혀 덜 받은 셀의 부족분은 <b>다음 셀의 몫에 그대로 들어간다.</b> 어디에서도
        /// 반올림 잔여를 버리지 않는다.
        /// </summary>
        /// <param name="recordReceipt">
        /// 참이면 쓴 값을 영수증에 적는다 — <b>되찾을 수 있는</b> 힙이다. 거짓이면 적지 않는다 —
        /// 측면 벽·소반처럼 <b>영구 필드 질량</b>이 되어 다음 지우기가 찾지 못한다.
        /// </param>
        /// <returns>실제로 필드에 들어간 양(cm).</returns>
        public long PlaceScanned(long amountCm, bool recordReceipt)
        {
            if (amountCm <= 0 || _scanCount == 0 || _scanWeightSum <= 0) return 0;

            int width = _field.Width;
            long acc = 0;
            long placed = 0;

            for (int i = 0; i < _scanCount; i++)
            {
                acc += _scanWeight[i];

                long ideal = amountCm * acc / _scanWeightSum;
                int want = (int)(ideal - placed);
                if (want <= 0) continue;

                int cell = _scanCell[i];
                int x = cell % width;
                int y = cell / width;

                int applied = _field.ApplyCellDelta(x, y, want);
                if (applied <= 0) continue;

                placed += applied;

                if (!recordReceipt) continue;

                _receiptCm[cell] += applied;
                _receiptSumCm += applied;
                if (x < _receiptMinX) _receiptMinX = x;
                if (y < _receiptMinY) _receiptMinY = y;
                if (x > _receiptMaxX) _receiptMaxX = x;
                if (y > _receiptMaxY) _receiptMaxY = y;
            }

            // 아무 셀도 못 받은 양. 원장에 남으므로 불변식은 안전하고, 이 값이 0 이 아니면
            // 발자국이 천장에 닿았다는 뜻이다.
            long unplaced = amountCm - placed;
            if (unplaced > 0)
            {
                _unplacedCm += unplaced;
                if (_unplacedCm > _unplacedPeakCm) _unplacedPeakCm = _unplacedCm;
            }

            return placed;
        }

        /// <summary>스캔한 집합에 원장에서 <paramref name="amountCm"/> 를 <b>힙으로</b> 놓는다.</summary>
        public long EmitFromCarried(long amountCm, bool recordReceipt)
        {
            if (amountCm > _carriedCm) amountCm = _carriedCm;

            long placed = PlaceScanned(amountCm, recordReceipt);
            _carriedCm -= placed;
            return placed;
        }

        /// <summary>스캔한 집합에 소반 예약분을 놓는다. 영수증은 적지 않는다 — 소반은 되찾지 않는다.</summary>
        public long EmitFromBerm(long amountCm)
        {
            if (amountCm > _bermCm) amountCm = _bermCm;

            long placed = PlaceScanned(amountCm, recordReceipt: false);
            _bermCm -= placed;
            return placed;
        }

        // ------------------------------------------------------------------ 시동 자기검사

        /// <summary>
        /// 합성 수열 하나로 <b>장부 전체를 시동 때 한 번</b> 돌려 보고 한 줄로 판정한다.
        /// <c>UnityEngine</c> 을 쓰지 않으므로 데디 서버·EditMode 테스트에서도 그대로 부를 수 있다.
        ///
        /// <b>왜 불변량 계기만으로는 부족한가.</b> 계기는 정확히 동작했다 — 정지한 차량이
        /// <c>-2542 L</c> 로 앉아 <c>LEAK</c> 을 찍고 있었다. 그런데 그 값이 <b>기준선이 이미
        /// 망가진 것</b>인지 시뮬레이션이 새고 있는 것인지는 사람이 몰고 나가 기울기를 볼 때까지
        /// 구별되지 않았다. 이것은 <b>아무도 몰기 전에</b>, 알려진 수열에서 알려진 답을 요구해
        /// 그 구별을 없앤다: 여기서 FAIL 이면 결함은 커널에 있고, 여기서 PASS 인데 씬에서
        /// LEAK 이면 결함은 <b>필드에 손대는 다른 무언가</b>에 있다.
        ///
        /// 프레임 경로가 아니다. 96×96 셀 격자에 relax 몇 번이라 1ms 아래이고 한 번만 돈다.
        /// </summary>
        /// <param name="report">한 줄 판정. 통과든 실패든 그대로 로그에 넣으면 된다.</param>
        public static bool SelfCheck(out string report)
        {
            string failure = RunSelfCheck(out string detail);
            report = failure is null
                ? "ledger selfcheck=PASS " + detail
                : "ledger selfcheck=FAIL " + failure;
            return failure is null;
        }

        /// <returns>실패한 단계의 설명, 통과면 <c>null</c>.</returns>
        private static string RunSelfCheck(out string detail)
        {
            const float cellSize = 0.0625f;
            const int cellsX = 96, cellsY = 96;
            const byte maxDepthCm = 200, startDepthCm = 20;
            const float bladeWidthM = 2.3f;
            const float halfWidthM = bladeWidthM * 0.5f;

            float tanFront = Tan(65f), tanBack = Tan(75f), tanRepose = Tan(55f);

            var field = new SnowField(0f, 0f, cellsX * cellSize, cellsY * cellSize, cellSize, maxDepthCm);
            field.FillAll(startDepthCm);

            var ledger = new SnowPlowLedger(field);
            var repose = new SnowRepose();

            detail = string.Empty;

            long pristine = (long)startDepthCm * cellsX * cellsY;
            if (field.TotalDepthCm != pristine)
                return $"fill: field={field.TotalDepthCm} expected={pristine} ccell";
            if (ledger.InvariantCm != 0) return $"reset: invariant={ledger.InvariantCm} ccell";

            float cx = cellsX * cellSize * 0.5f;
            float cz = cellsY * cellSize * 0.5f;

            // 1 자르기 — 필드에서 빠진 정수가 conserved + berm + deleted 로 정확히 갈라져야 한다
            var cutArea = new SnowStampArea(cx, cz, 0f, 1f, 0.175f, halfWidthM);
            int removed = field.ApplyStamp(1, 1, cutArea, -60);
            if (removed <= 0) return "cut: removed=0 (발자국이 격자를 벗어났다)";
            ledger.CreditCut(removed, 400, 350);
            if (ledger.InvariantCm != 0) return $"cut: invariant={ledger.InvariantCm} ccell removed={removed}";
            if (ledger._carriedCm + ledger._bermCm + ledger._deletedCm != removed)
                return $"cut: 갈라진 합 {ledger._carriedCm}+{ledger._bermCm}+{ledger._deletedCm} ≠ removed {removed}";

            // 2 힙 방출 — 필드에 쓴 정수만큼만 원장에서 빠져야 하고 unplaced 는 0 이어야 한다
            float peakM = 0.5f;
            var shape = new SnowHeapShape(peakM, halfWidthM, tanFront, tanBack, tanRepose);
            ScanShape(ledger, field, shape, cx, cz, out int x0, out int y0, out int x1, out int y1);
            if (ledger.ScannedCells <= 0) return "emit: 스캔 집합이 비었다";

            long want = ledger.CarriedCm;
            if (want > ledger.ScannedCapacityCm) want = ledger.ScannedCapacityCm;
            long placed = ledger.EmitFromCarried(want, recordReceipt: true);
            if (placed <= 0) return $"emit: placed=0 (want={want}, room={ledger.ScannedCapacityCm})";
            if (ledger.InvariantCm != 0) return $"emit: invariant={ledger.InvariantCm} ccell";
            if (ledger._unplacedCm != 0) return $"emit: unplaced={ledger.UnplacedLitres:F4}L";
            if (ledger._receiptSumCm != placed)
                return $"emit: 영수증 합 {ledger._receiptSumCm} ≠ placed {placed} ccell";

            // 3 relax — 짝마다 정수 하나가 오가므로 필드 합이 정확히 보존돼야 한다.
            //   가드를 끄고 돈다: 앞면 65° 가 안식각 55° 로 실제로 주저앉아 흐름이 0 이 아니어야
            //   검사가 공허하지 않다.
            long fieldBefore = field.TotalDepthCm;
            long invBefore = ledger.InvariantCm;
            int flows = 0;
            int maxDelta = Round(tanRepose * cellSize * 100f);
            int maxDeltaDiag = Round(tanRepose * cellSize * 141.421356f);
            repose.Touch(x0, y0, x1, y1, 0f);
            for (int i = 0; i < 8; i++)
            {
                repose.Run(field, ledger, 0f, 10f, 4, 0, 4, 110,
                           maxDelta, maxDeltaDiag, maxDelta, maxDeltaDiag, guardEnabled: false);
                flows += repose.Flows;
            }
            if (flows <= 0) return "relax: 흐름이 0 이다 (검사가 아무것도 재지 않았다)";
            if (field.TotalDepthCm != fieldBefore)
                return $"relax: 필드 합이 {field.TotalDepthCm - fieldBefore} ccell 움직였다 (flows={flows})";
            if (ledger.InvariantCm != invBefore) return $"relax: invariant={ledger.InvariantCm} ccell";

            // 4 지우기 — 영수증을 읽어 정확히 그만큼만 되찾는다
            long back = ledger.ErasePaidReceipt();
            if (back <= 0) return "erase: back=0";
            if (back > placed) return $"erase: back={back} > placed={placed}";
            if (ledger.InvariantCm != 0) return $"erase: invariant={ledger.InvariantCm} ccell";
            if (ledger.HasReceipt) return "erase: 영수증이 남았다";

            // 5 퇴역(내려놓기) — 필드도 원장도 건드리지 않으므로 불변식이 1 ccell 도 안 움직인다
            ScanShape(ledger, field, shape, cx, cz, out _, out _, out _, out _);
            long standing = ledger.EmitFromCarried(
                ledger.CarriedCm < ledger.ScannedCapacityCm ? ledger.CarriedCm : ledger.ScannedCapacityCm,
                recordReceipt: true);
            fieldBefore = field.TotalDepthCm;
            invBefore = ledger.InvariantCm;
            ledger.RetireReceipt();
            if (field.TotalDepthCm != fieldBefore) return "retire: 필드가 움직였다";
            if (ledger.InvariantCm != invBefore) return $"retire: invariant={ledger.InvariantCm} ccell";
            if (ledger.HasReceipt) return "retire: 영수증이 남았다";

            // 6 소반 — 영수증 없는 영구 질량. 놓인 만큼만 예약에서 빠져야 한다
            long bermBefore = ledger.BermCm;
            ScanCone(ledger, field, cx + halfWidthM + 1.25f, cz - 0.9f, 0.55f, tanRepose);
            long bermPlaced = ledger.EmitFromBerm(ledger.BermCm);
            if (bermPlaced <= 0) return "berm: placed=0";
            if (ledger.BermCm != bermBefore - bermPlaced) return "berm: 예약이 놓인 양과 안 맞는다";
            if (ledger.InvariantCm != 0) return $"berm: invariant={ledger.InvariantCm} ccell";

            // 7 측면 벽(방출) — 원장에서 나가고 영수증은 안 남는다.
            //   퇴역이 원장을 비웠으므로 처녀설을 한 번 더 잘라 원장을 채운다.
            int removed2 = field.ApplyStamp(2, 1, new SnowStampArea(cx, cz + 1f, 0f, 1f, 0.175f, halfWidthM), -60);
            if (removed2 <= 0) return "release: 두 번째 자르기가 0 이다";
            ledger.CreditCut(removed2, 1000, 0);
            if (ledger.InvariantCm != 0) return $"release: 자르기 후 invariant={ledger.InvariantCm} ccell";

            ScanCone(ledger, field, cx - halfWidthM - 0.95f, cz - 0.45f, 0.85f, tanRepose);
            long spillRoom = ledger.ScannedCapacityCm;
            long spilled = ledger.EmitFromCarried(
                ledger.CarriedCm < spillRoom ? ledger.CarriedCm : spillRoom, recordReceipt: false);
            if (spilled <= 0) return $"release: placed=0 (carried={ledger.CarriedCm}, room={spillRoom})";
            if (ledger.HasReceipt) return "release: 영수증이 생겼다";
            if (ledger.InvariantCm != 0) return $"release: invariant={ledger.InvariantCm} ccell";

            // 8 천장 — 여유가 0 인 셀은 분모에 들어가지 않는다. 그래서 빈 집합에 놓으려 해도
            //   아무것도 놓이지 않고 <b>아무것도 사라지지 않는다</b>.
            int fullX = 2, fullY = 2;
            int lifted = field.ApplyCellDelta(fullX, fullY, maxDepthCm);   // 여유를 0 으로 만든다
            long invCeiling = ledger.InvariantCm;
            ledger.BeginScan();
            ledger.ScanCell(fullX, fullY, 1f);
            if (ledger.ScannedCells != 0) return "ceiling: 여유 0 인 셀이 분모에 들어갔다";
            if (ledger.PlaceScanned(1000, recordReceipt: false) != 0) return "ceiling: 빈 집합에 놓았다";
            if (ledger.InvariantCm != invCeiling || ledger._unplacedCm != 0)
                return $"ceiling: invariant={ledger.InvariantCm} ccell unplaced={ledger._unplacedCm}";
            field.ApplyCellDelta(fullX, fullY, -lifted);          // 올린 정수만큼만 되돌린다

            // 9 발자국 폭 — 정상 길이는 <b>부피와 무관</b>하다. 폭 노브가 생기면 여기서 즉시 걸린다
            for (float h = 0.01f; h <= 2.5f; h += 0.07f)
            {
                var probe = new SnowHeapShape(h, halfWidthM, tanFront, tanBack, tanRepose);
                if (probe.HalfCrestM * 2f != bladeWidthM)
                    return $"footprint: h={h:F2}m 에서 정상 길이 {probe.HalfCrestM * 2f:F4}m ≠ {bladeWidthM:F3}m";
                if (probe.AcrossHalfM < probe.HalfCrestM)
                    return $"footprint: h={h:F2}m 에서 지지 반폭이 정상 반길이보다 작다";
            }

            if (ledger.InvariantCm != 0) return $"final: invariant={ledger.InvariantCm} ccell";
            if (ledger._unplacedPeakCm != 0)
                return $"unplaced: 피크 {ledger.UnplacedPeakLitres:F4}L (0 이어야 한다)";

            detail = $"9단계 invariant=0ccell unplacedPeak=0.0000L relaxFlows={flows} "
                   + $"footprint={bladeWidthM:F3}m(h 0.01~2.50m 고정) "
                   + $"quantum={ledger.LitresPerCm:F4}L";
            return null;
        }

        private static void ScanShape(SnowPlowLedger ledger, SnowField field, in SnowHeapShape shape,
                                      float crestX, float crestZ,
                                      out int x0, out int y0, out int x1, out int y1)
        {
            float along = shape.AheadM > shape.BehindM ? shape.AheadM : shape.BehindM;
            x0 = Clamp(field.CellXAtWorld(crestX - shape.AcrossHalfM) - 2, 0, field.Width - 1);
            x1 = Clamp(field.CellXAtWorld(crestX + shape.AcrossHalfM) + 2, 0, field.Width - 1);
            y0 = Clamp(field.CellYAtWorld(crestZ - along) - 2, 0, field.Height - 1);
            y1 = Clamp(field.CellYAtWorld(crestZ + along) + 2, 0, field.Height - 1);

            ledger.BeginScan();
            for (int y = y0; y <= y1; y++)
            {
                float wz = field.WorldZAtCell(y);
                for (int x = x0; x <= x1; x++)
                    ledger.ScanCell(x, y, shape.HeightM(wz - crestZ, field.WorldXAtCell(x) - crestX));
            }
        }

        private static void ScanCone(SnowPlowLedger ledger, SnowField field,
                                     float centreX, float centreZ, float radiusM, float tanRepose)
        {
            int x0 = Clamp(field.CellXAtWorld(centreX - radiusM) - 2, 0, field.Width - 1);
            int x1 = Clamp(field.CellXAtWorld(centreX + radiusM) + 2, 0, field.Width - 1);
            int y0 = Clamp(field.CellYAtWorld(centreZ - radiusM) - 2, 0, field.Height - 1);
            int y1 = Clamp(field.CellYAtWorld(centreZ + radiusM) + 2, 0, field.Height - 1);

            ledger.BeginScan();
            for (int y = y0; y <= y1; y++)
            {
                float dz = field.WorldZAtCell(y) - centreZ;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = field.WorldXAtCell(x) - centreX;
                    float d = MathF.Sqrt(dx * dx + dz * dz);
                    ledger.ScanCell(x, y, d >= radiusM ? 0f : (radiusM - d) * tanRepose);
                }
            }
        }

        private static float Tan(float degrees) => MathF.Tan(degrees * (MathF.PI / 180f));
        private static int Round(float v) => (int)MathF.Round(v) < 1 ? 1 : (int)MathF.Round(v);
        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        private void ClearReceiptRect()
        {
            _receiptMinX = int.MaxValue;
            _receiptMinY = int.MaxValue;
            _receiptMaxX = int.MinValue;
            _receiptMaxY = int.MinValue;
        }
    }
}
