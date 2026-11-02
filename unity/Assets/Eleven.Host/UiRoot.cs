using System;
using Eleven.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eleven.Host
{
    /// <summary>
    /// The UI around the match (#48): one UI Toolkit panel, built entirely from code. Its
    /// PanelSettings and an empty theme are created at runtime, so there are no UXML or USS assets
    /// and no Editor state. Components style themselves from <see cref="Theme"/> roles through the
    /// recipes in <see cref="UiKit"/>.
    ///
    /// Lengths are points; <see cref="SetPixelsPerPoint"/> keeps a point the size of a CSS pixel on
    /// the web. Input reaches the panel through Unity 6's InputForUI from the Input System, so there is
    /// no EventSystem.
    /// </summary>
    internal sealed class UiRoot
    {
        private readonly PanelSettings _settings;

        public UiRoot(Transform parent, Theme theme)
        {
            Theme = theme;
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "ELEVEN UI";
            _settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.scale = 1;
            _settings.sortingOrder = (int)Layer.Controls;

            var host = new GameObject("UI");
            host.SetActive(false);
            host.transform.SetParent(parent, false);
            Document = host.AddComponent<UIDocument>();
            Document.panelSettings = _settings;
            host.SetActive(true);

            Root = Document.rootVisualElement;
            Root.pickingMode = PickingMode.Ignore;
            Root.style.position = Position.Absolute;
            Root.style.left = Root.style.top = Root.style.right = Root.style.bottom = 0;
            Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Root.style.unityFontDefinition = FontDefinition.FromFont(Font);

            // Focus came from the keyboard until a pointer says otherwise.
            Root.RegisterCallback<PointerDownEvent>(_ => KeyboardFocus = false, TrickleDown.TrickleDown);
            Root.RegisterCallback<NavigationMoveEvent>(_ => KeyboardFocus = true, TrickleDown.TrickleDown);
            Root.RegisterCallback<KeyDownEvent>(_ => KeyboardFocus = true, TrickleDown.TrickleDown);
        }

        /// <summary>Whether the latest input was a key or navigation, so focus rings should show.</summary>
        public bool KeyboardFocus { get; private set; }

        public Theme Theme { get; }

        /// <summary>
        /// The player's reduced-motion preference: movement is removed and a static cue stays
        /// (pressed controls lose their shadow instead of moving into it).
        /// </summary>
        public static bool ReducedMotion { get; set; }

        /// <summary>The player's text size, as a multiple of the type scale (enlarged text reflows; targets never shrink).</summary>
        public static float TextScale { get; set; } = 1;

        public UIDocument Document { get; }

        /// <summary>Everything the UI draws hangs from here.</summary>
        public VisualElement Root { get; }

        /// <summary>The built-in runtime font (#48: no font asset until the device review asks for one).</summary>
        public Font Font { get; }

        public float PixelsPerPoint => _settings.scale;

        public void SetPixelsPerPoint(float pixelsPerPoint) => _settings.scale = Mathf.Max(0.25f, pixelsPerPoint);

        /// <summary>Whether a point in HUD pixels (top-left origin) lands on a UI element that takes input.</summary>
        public bool IsOverUi(Vector2 hudPixels)
        {
            IPanel panel = Root.panel;
            if (panel == null) return false;
            VisualElement picked = panel.Pick(hudPixels / PixelsPerPoint);
            return picked != null && picked != Root;
        }

        /// <summary>A HUD rect in pixels as an absolutely placed element's position and size, in points.</summary>
        public void Place(VisualElement element, Rect hudPixels)
        {
            float s = PixelsPerPoint;
            element.style.position = Position.Absolute;
            element.style.left = hudPixels.x / s;
            element.style.top = hudPixels.y / s;
            element.style.width = Mathf.Max(Interaction.MinTarget, hudPixels.width / s);
            element.style.height = Mathf.Max(Interaction.MinTarget, hudPixels.height / s);
        }
    }
}
