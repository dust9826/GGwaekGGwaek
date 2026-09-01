using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PPack
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuCursor : MonoBehaviour
    {
        [SerializeField] private Texture2D _defaultCursor;
        [SerializeField] private Vector2 _hotspot = new Vector2(1.5f, 1.5f);
        [SerializeField] private Color _hoverTint = new Color(0.333f, 0.749f, 0.886f, 1f);
        [SerializeField] private Color _clickTint = new Color(0.184f, 0.514f, 0.812f, 1f);

        private readonly List<Button> _buttons = new List<Button>();
        private VisualElement _root;
        private Texture2D _hoverCursor;
        private Texture2D _clickCursor;
        private Texture2D _currentCursor;
        private bool _hoveringButton;
        private bool _pressingButton;

        private void OnEnable()
        {
            if (!TryGetComponent(out UIDocument document) || _defaultCursor == null)
            {
                enabled = false;
                return;
            }

            _root = document.rootVisualElement;
            _root.Query<Button>(className: "flow-button").ToList(_buttons);

            foreach (Button button in _buttons)
            {
                button.RegisterCallback<PointerEnterEvent>(OnButtonEnter);
                button.RegisterCallback<PointerLeaveEvent>(OnButtonLeave);
                button.RegisterCallback<PointerDownEvent>(OnButtonDown);
                button.RegisterCallback<PointerUpEvent>(OnButtonUp);
            }

            CreateRuntimeCursors();
            ApplyCurrentCursor();
        }

        private void OnDisable()
        {
            foreach (Button button in _buttons)
            {
                button.UnregisterCallback<PointerEnterEvent>(OnButtonEnter);
                button.UnregisterCallback<PointerLeaveEvent>(OnButtonLeave);
                button.UnregisterCallback<PointerDownEvent>(OnButtonDown);
                button.UnregisterCallback<PointerUpEvent>(OnButtonUp);
            }

            _buttons.Clear();
            _root = null;
            _currentCursor = null;
            _hoveringButton = false;
            _pressingButton = false;
            UnityEngine.Cursor.visible = true;
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            DestroyRuntimeCursors();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                _pressingButton = false;
                _currentCursor = null;
                return;
            }

            if (isActiveAndEnabled)
            {
                ApplyCurrentCursor();
            }
        }

        private void OnButtonEnter(PointerEnterEvent _)
        {
            _hoveringButton = true;
            ApplyCurrentCursor();
        }

        private void OnButtonLeave(PointerLeaveEvent _)
        {
            _hoveringButton = false;
            ApplyCurrentCursor();
        }

        private void OnButtonDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            _pressingButton = true;
            ApplyCurrentCursor();
        }

        private void OnButtonUp(PointerUpEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            _pressingButton = false;
            ApplyCurrentCursor();
        }

        private void ApplyCurrentCursor()
        {
            Texture2D texture = _pressingButton
                ? _clickCursor
                : _hoveringButton
                    ? _hoverCursor
                    : _defaultCursor;

            texture ??= _defaultCursor;
            if (texture == _currentCursor)
            {
                return;
            }

            _currentCursor = texture;
            UnityEngine.Cursor.visible = true;
            UnityEngine.Cursor.SetCursor(texture, _hotspot, CursorMode.Auto);
        }

        private void CreateRuntimeCursors()
        {
            DestroyRuntimeCursors();
            _hoverCursor = CreateTintedCursor(_defaultCursor, _hoverTint, "MainMenuCursor_Hover_Runtime");
            _clickCursor = CreateTintedCursor(_defaultCursor, _clickTint, "MainMenuCursor_Click_Runtime");
        }

        private static Texture2D CreateTintedCursor(Texture2D source, Color tint, string cursorName)
        {
            Color32[] pixels = source.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (pixel.a == 0)
                {
                    continue;
                }

                pixel.r = (byte)Mathf.RoundToInt(pixel.r * tint.r);
                pixel.g = (byte)Mathf.RoundToInt(pixel.g * tint.g);
                pixel.b = (byte)Mathf.RoundToInt(pixel.b * tint.b);
                pixels[i] = pixel;
            }

            Texture2D texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false)
            {
                name = cursorName,
                filterMode = source.filterMode,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private void DestroyRuntimeCursors()
        {
            if (_hoverCursor != null)
            {
                Destroy(_hoverCursor);
                _hoverCursor = null;
            }

            if (_clickCursor != null)
            {
                Destroy(_clickCursor);
                _clickCursor = null;
            }
        }
    }
}
