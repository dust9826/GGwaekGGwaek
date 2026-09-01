using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// <see cref="SnowStage"/> 의 권위 격자를 셰이더가 읽는 RG8 텍스처로 올린다.
    ///
    /// <b>이것은 파생물이고 권위가 아니다.</b> 어떤 판정도 이 텍스처를 읽지 않는다 —
    /// 데디 서버에는 GPU 가 없어서 애초에 존재하지 않는다(<c>docs/specs/2026-08-14-snow-surface.md</c> §5 규약 2).
    ///
    /// 그래픽 디바이스가 없으면 <b>스스로 꺼진다.</b> 먼지의 <c>DustPaintTarget.Awake</c> 는
    /// <c>RenderTexture</c> 를 무조건 만들고 <c>Shader.Find</c> 를 부르는데, 눈에서 그 실수를 반복하지 않는다.
    ///
    /// 필드 파라미터는 <b>머티리얼이 아니라 전역</b>으로 싣는다. 그래야 패널 여러 장이 머티리얼
    /// 한 장을 공유해도 SRP Batcher 가 묶는다.
    /// </summary>
    [RequireComponent(typeof(SnowStage))]
    public sealed class SnowSurfaceRenderer : MonoBehaviour
    {
        [Tooltip("R 채널에 실을 신선도의 감쇠 시간(초). 방금 밀린 자국이 다져진 톤으로 보이는 기간이다.\n" +
                 "권위가 아니라 클라 로컬 연출이므로 클라마다 달라도 무해하다.")]
        [SerializeField, Min(0.1f)] private float _freshnessSeconds = 3f;

        private static readonly int FieldId = Shader.PropertyToID("_SnowField");
        private static readonly int OriginId = Shader.PropertyToID("_SnowFieldOrigin");
        private static readonly int InvSizeId = Shader.PropertyToID("_SnowFieldInvSize");
        private static readonly int TexelSizeId = Shader.PropertyToID("_SnowFieldTexelSize");
        private static readonly int CellSizeId = Shader.PropertyToID("_SnowFieldCellSize");
        private static readonly int TileMaxId = Shader.PropertyToID("_SnowTileMax");
        private static readonly int TileParamsId = Shader.PropertyToID("_SnowTileParams");

        [Header("레이마칭 (PPack/SnowRaymarch 를 쓸 때만 의미가 있다)")]
        [Tooltip("타일 최대 높이 피라미드의 밉 0 타일 크기(m). 하네스 실측에서 0.5m 가 가장 좋았다.\n" +
                 "빈 공간 건너뛰기의 해상도이고, 굵으면 건너뛰기가 덜 걸리고 잘면 텍스처가 커진다.")]
        [SerializeField, Min(0.125f)] private float _tileMeters = 0.5f;

        private SnowStage _stage;
        private Texture2D _texture;
        private Color32[] _pixels;
        private float[] _freshness;      // 셀당 남은 신선도 0~1. 클라 로컬.
        // 감쇠 중인 셀만 담는 활성 목록. 이게 없으면 매 프레임 격자 전체를 훑게 되고,
        // 셀을 12.5cm → 6.25cm 로 올리는 순간 프레임당 100 만 회가 된다(스펙 §5: 갱신은 희소하게).
        private readonly System.Collections.Generic.List<int> _freshCells = new();
        // 타일 최대 높이 피라미드. **밉이 평균이 아니라 최대**로 채워져 있어야 상한 보장이 성립한다.
        // 마처가 이것으로 빈 공간을 건너뛴다 — 없으면 평균 스텝이 7 에서 140 으로 뛴다(하네스 실측).
        private Texture2D _tileMax;
        private byte[][] _tileMips;
        private int _tileWidth;
        private int _tileHeight;
        private int _tileMipCount;
        private int _cellsPerTile;
        private bool _fullUploadPending;

        private void Awake()
        {
            _stage = GetComponent<SnowStage>();

            // 헤드리스 서버에서는 텍스처를 만들지 않는다. 권위는 SnowStage 에 있고 여기는 연출이다.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) enabled = false;
        }

        /// <summary>
        /// 텍스처와 전역 파라미터를 만든다. <b>Awake 에서 하지 않는다</b> — 같은 GameObject 위
        /// 컴포넌트의 <c>Awake</c> 순서는 보장되지 않아서, 이 컴포넌트가 <see cref="SnowStage"/> 보다
        /// 먼저 깨면 <c>_stage.Field</c> 가 null 이다.
        ///
        /// 실제로 그렇게 물렸다(2026-08-14). 증상이 고약한 이유는 예외가 조용히 한 줄 찍히고 끝나는
        /// 대신 <b>이전 씬이 남긴 셰이더 전역</b>으로 계속 그려지기 때문이다 — 전역은 에디터 세션 동안
        /// 살아남으므로, 16×16m 게이트 필드가 64×64m 패널에 그려져 표면이 노이즈에 먹힌 것처럼 보였다.
        /// 해상도 문제로 오진하기 쉬운 실패 모드다.
        /// </summary>
        private bool EnsureTexture()
        {
            if (_texture != null) return true;
            if (_stage == null) return false;

            SnowField field = _stage.Field;
            if (field == null) return false;
            _texture = new Texture2D(field.Width, field.Height, TextureFormat.RG16,
                                     mipChain: true, linear: true)
            {
                name = "SnowField",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };

            _pixels = new Color32[field.Width * field.Height];
            _freshness = new float[field.Width * field.Height];
            _fullUploadPending = true;

            Shader.SetGlobalTexture(FieldId, _texture);
            Shader.SetGlobalVector(OriginId, new Vector4(_stage.OriginXZ.x, _stage.OriginXZ.y, 0f, 0f));
            Shader.SetGlobalVector(InvSizeId,
                new Vector4(1f / _stage.SizeMeters.x, 1f / _stage.SizeMeters.y, 0f, 0f));
            Shader.SetGlobalVector(TexelSizeId,
                new Vector4(1f / field.Width, 1f / field.Height, field.Width, field.Height));
            Shader.SetGlobalFloat(CellSizeId, _stage.CellSize);
            EnsureTilePyramid(field);
            return true;
        }

        /// <summary>
        /// 타일 최대 높이 피라미드를 만든다. <b>밉을 유니티에 맡기지 않는다</b> — 자동 밉은 평균이고,
        /// 평균은 상한이 아니라서 마처가 표면을 뚫는다. 레벨마다 자식 4개의 <b>최대</b>를 쓴다.
        ///
        /// 밉 0 은 3×3 팽창한다. 워프가 샘플 좌표를 최대 <c>2 × _FieldWarp</c>(0.2m) 밖으로 끌고 가므로,
        /// 팽창 없이는 타일 경계 근처에서 상한이 깨진다.
        /// </summary>
        private void EnsureTilePyramid(SnowField field)
        {
            if (_tileMax != null) return;

            _cellsPerTile = Mathf.Max(1, Mathf.RoundToInt(_tileMeters / _stage.CellSize));
            _tileWidth = Mathf.Max(1, (field.Width + _cellsPerTile - 1) / _cellsPerTile);
            _tileHeight = Mathf.Max(1, (field.Height + _cellsPerTile - 1) / _cellsPerTile);
            _tileMipCount = 1 + Mathf.FloorToInt(Mathf.Log(Mathf.Max(_tileWidth, _tileHeight), 2f));

            _tileMax = new Texture2D(_tileWidth, _tileHeight, TextureFormat.R8,
                                     _tileMipCount, linear: true)
            {
                name = "SnowTileMax",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,      // 상한은 보간하면 안 된다 — 평균이 되어 깨진다
                hideFlags = HideFlags.DontSave,
            };

            _tileMips = new byte[_tileMipCount][];
            int w = _tileWidth, h = _tileHeight;
            for (int mip = 0; mip < _tileMipCount; mip++)
            {
                _tileMips[mip] = new byte[Mathf.Max(1, w) * Mathf.Max(1, h)];
                w = Mathf.Max(1, w >> 1);
                h = Mathf.Max(1, h >> 1);
            }

            Shader.SetGlobalTexture(TileMaxId, _tileMax);
            Shader.SetGlobalVector(TileParamsId,
                new Vector4(_cellsPerTile * _stage.CellSize, _tileMipCount, 0f, 0f));
        }

        /// <summary>피라미드를 다시 굽는다. 비용은 타일 수에 비례한다 — 격자 셀 수의 1/16(0.5m 타일).</summary>
        private void RebuildTilePyramid(SnowField field, float depthInv)
        {
            if (_tileMax == null) return;

            // 밉 0 — 타일 안 셀의 최대. 경계를 넘겨 한 셀 더 본다(바이리니어 이웃).
            byte[] level0 = _tileMips[0];
            for (int ty = 0; ty < _tileHeight; ty++)
            {
                for (int tx = 0; tx < _tileWidth; tx++)
                {
                    int max = 0;
                    int cx0 = tx * _cellsPerTile, cy0 = ty * _cellsPerTile;
                    for (int cy = cy0; cy <= cy0 + _cellsPerTile; cy++)
                    {
                        for (int cx = cx0; cx <= cx0 + _cellsPerTile; cx++)
                        {
                            int d = field.DepthCmAtCell(Mathf.Min(cx, field.Width - 1),
                                                        Mathf.Min(cy, field.Height - 1));
                            if (d > max) max = d;
                        }
                    }

                    level0[ty * _tileWidth + tx] =
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(max * depthInv) * 255f);
                }
            }

            // 3×3 팽창. 워프가 타일 밖을 읽는 만큼을 덮는다.
            var dilated = new byte[level0.Length];
            for (int ty = 0; ty < _tileHeight; ty++)
            {
                for (int tx = 0; tx < _tileWidth; tx++)
                {
                    byte max = 0;
                    for (int dy2 = -1; dy2 <= 1; dy2++)
                    {
                        int sy = Mathf.Clamp(ty + dy2, 0, _tileHeight - 1);
                        for (int dx2 = -1; dx2 <= 1; dx2++)
                        {
                            int sx = Mathf.Clamp(tx + dx2, 0, _tileWidth - 1);
                            byte v = level0[sy * _tileWidth + sx];
                            if (v > max) max = v;
                        }
                    }

                    dilated[ty * _tileWidth + tx] = max;
                }
            }

            System.Array.Copy(dilated, level0, level0.Length);
            _tileMax.SetPixelData(level0, 0);

            // 상위 밉 — 자식 4개의 최대. **평균이 아니다.**
            int pw = _tileWidth, ph = _tileHeight;
            for (int mip = 1; mip < _tileMipCount; mip++)
            {
                int cw = Mathf.Max(1, pw >> 1);
                int ch = Mathf.Max(1, ph >> 1);
                byte[] parent = _tileMips[mip - 1];
                byte[] child = _tileMips[mip];

                for (int y = 0; y < ch; y++)
                {
                    for (int x = 0; x < cw; x++)
                    {
                        int sx = x << 1, sy = y << 1;
                        byte a = parent[Mathf.Min(sy, ph - 1) * pw + Mathf.Min(sx, pw - 1)];
                        byte b = parent[Mathf.Min(sy, ph - 1) * pw + Mathf.Min(sx + 1, pw - 1)];
                        byte c = parent[Mathf.Min(sy + 1, ph - 1) * pw + Mathf.Min(sx, pw - 1)];
                        byte d = parent[Mathf.Min(sy + 1, ph - 1) * pw + Mathf.Min(sx + 1, pw - 1)];
                        child[y * cw + x] = Math.Max(Math.Max(a, b), Math.Max(c, d));
                    }
                }

                _tileMax.SetPixelData(child, mip);
                pw = cw;
                ph = ch;
            }

            _tileMax.Apply(updateMipmaps: false);
        }

        /// <summary>
        /// 방금 밀린 자리를 다져진 톤으로 그리기 위한 신선도. <b>권위가 아니다.</b>
        /// 도구가 제거한 셀 범위를 넘겨주면 그 범위를 1로 채운다.
        /// </summary>
        public void MarkFresh(in SnowStampArea area)
        {
            if (!enabled || !EnsureTexture()) return;

            SnowField field = _stage.Field;
            int minX = Mathf.Max(0, Mathf.FloorToInt((area.MinX - _stage.OriginXZ.x) / _stage.CellSize));
            int maxX = Mathf.Min(field.Width - 1, Mathf.FloorToInt((area.MaxX - _stage.OriginXZ.x) / _stage.CellSize));
            int minY = Mathf.Max(0, Mathf.FloorToInt((area.MinZ - _stage.OriginXZ.y) / _stage.CellSize));
            int maxY = Mathf.Min(field.Height - 1, Mathf.FloorToInt((area.MaxZ - _stage.OriginXZ.y) / _stage.CellSize));

            for (int y = minY; y <= maxY; y++)
            {
                int row = y * field.Width;
                for (int x = minX; x <= maxX; x++)
                {
                    int i = row + x;
                    if (_freshness[i] <= 0f) _freshCells.Add(i);
                    _freshness[i] = 1f;
                }
            }
        }

        // 프레임당 한 번. FixedUpdate 에 두면 재시뮬레이션마다 중복으로 도는데, 이건 연출이라
        // 권위와 달리 중복이 무해하지 않고 그냥 낭비다.
        private void LateUpdate()
        {
            if (!EnsureTexture()) return;
            SnowField field = _stage.Field;

            var (dx, dy, dw, dh) = field.DirtyRect;
            bool full = _fullUploadPending || (dw >= field.Width && dh >= field.Height);

            // 신선도는 스탬프가 끝난 뒤에도 감쇠하므로, 스탬프 rect 만 갱신하면 다져진 톤이 굳는다.
            // 감쇠 중인 셀을 따로 모으는 대신 더티 rect 를 그쪽으로 넓힌다.
            float decay = Time.deltaTime / _freshnessSeconds;

            int x0 = full ? 0 : dx;
            int y0 = full ? 0 : dy;
            int x1 = full ? field.Width - 1 : dx + dw - 1;
            int y1 = full ? field.Height - 1 : dy + dh - 1;

            bool anyFresh = _freshCells.Count > 0;
            float inv = 1f / Mathf.Max(1, _stage.MaxDepthCm);

            // 감쇠는 활성 목록만 돈다 — 비용이 격자 크기가 아니라 **최근에 밀린 면적**에 비례한다.
            for (int k = _freshCells.Count - 1; k >= 0; k--)
            {
                int i = _freshCells[k];
                float f = Mathf.Max(0f, _freshness[i] - decay);
                _freshness[i] = f;

                int x = i % field.Width;
                int y = i / field.Width;
                if (x < x0) x0 = x;
                if (y < y0) y0 = y;
                if (x > x1) x1 = x;
                if (y > y1) y1 = y;

                if (f <= 0f)
                {
                    // 마지막 원소를 끌어와 O(1) 로 제거한다. 순서는 의미 없다.
                    _freshCells[k] = _freshCells[_freshCells.Count - 1];
                    _freshCells.RemoveAt(_freshCells.Count - 1);
                }
            }

            if (!full && !anyFresh && (dw == 0 || dh == 0)) return;

            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 >= field.Width) x1 = field.Width - 1;
            if (y1 >= field.Height) y1 = field.Height - 1;
            if (x1 < x0 || y1 < y0) return;

            for (int y = y0; y <= y1; y++)
            {
                int row = y * field.Width;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x;
                    byte depth01 = (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(field.DepthCmAtCell(x, y) * inv) * 255f);
                    byte fresh = (byte)Mathf.RoundToInt(Mathf.Clamp01(_freshness[i]) * 255f);
                    _pixels[i] = new Color32(depth01, fresh, 0, 255);
                }
            }

            // SetPixels32 는 부분 갱신이 되지만 Apply 는 밉 전체를 다시 만든다.
            // 12.5cm·64×64m 에서 512² RG8 = 512KB 이므로 지금은 이걸로 충분하다.
            // 스테이지가 이보다 커지면 밉 재생성 비용부터 재보고 결정한다.
            _texture.SetPixels32(_pixels);
            _texture.Apply(updateMipmaps: true);

            // 타일 피라미드는 필드가 바뀐 프레임에만 다시 굽는다. 비용은 타일 수(셀 수의 1/16)에
            // 비례하고, 여기가 마처의 "빈 공간 건너뛰기"가 성립하는 유일한 근거다.
            RebuildTilePyramid(field, inv);

            _fullUploadPending = false;
            field.ClearDirty();
        }

        private void OnDestroy()
        {
            if (_texture != null) Destroy(_texture);
            if (_tileMax != null) Destroy(_tileMax);
        }
    }
}
