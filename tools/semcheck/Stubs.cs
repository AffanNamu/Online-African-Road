using System;
using UnityEngine.Events;
namespace UnityEngine.EventSystems
{
    public class BaseEventData { }
    public class PointerEventData : BaseEventData { public Vector2 position, delta; public int pointerId; public GameObject pointerPress, pointerEnter; public float scrollDelta_y; public Vector2 scrollDelta; public int clickCount; public Camera pressEventCamera; public int button_; public InputButton button; public enum InputButton { Left, Right, Middle } }
    public interface IPointerClickHandler { void OnPointerClick(PointerEventData e); }
    public interface IPointerEnterHandler { void OnPointerEnter(PointerEventData e); }
    public interface IPointerExitHandler { void OnPointerExit(PointerEventData e); }
    public interface IPointerDownHandler { void OnPointerDown(PointerEventData e); }
    public interface IPointerUpHandler { void OnPointerUp(PointerEventData e); }
    public interface IDragHandler { void OnDrag(PointerEventData e); }
    public interface IBeginDragHandler { void OnBeginDrag(PointerEventData e); }
    public interface IEndDragHandler { void OnEndDrag(PointerEventData e); }
    public interface IScrollHandler { void OnScroll(PointerEventData e); }
    public struct RaycastResult { public GameObject gameObject; }
    public class EventSystem : MonoBehaviour { public static EventSystem current; public void RaycastAll(PointerEventData d, System.Collections.Generic.List<RaycastResult> r) { } public GameObject currentSelectedGameObject; public void SetSelectedGameObject(GameObject g) { } }
    public class StandaloneInputModule : MonoBehaviour { }
    public class BaseInputModule : MonoBehaviour { }
}
namespace UnityEngine.UI
{
    public class Graphic : MonoBehaviour { public Color color; public bool raycastTarget; public Material material; public virtual Texture mainTexture => null; public CanvasRenderer canvasRenderer; public RectTransform rectTransform => (RectTransform)transform; public void SetAllDirty() { } public void CrossFadeAlpha(float a, float d, bool i) { } }
    public class MaskableGraphic : Graphic { }
    public class Image : MaskableGraphic { public Sprite sprite; public bool preserveAspect; public enum Type { Simple, Sliced, Tiled, Filled } public Type type; public enum FillMethod { Horizontal } public FillMethod fillMethod; public float fillAmount; public bool fillCenter; public float pixelsPerUnitMultiplier; }
    public class RawImage : MaskableGraphic { public Texture texture; public Rect uvRect; }
    public class Text : MaskableGraphic
    {
        public string text; public Font font; public int fontSize; public FontStyle fontStyle; public TextAnchor alignment; public HorizontalWrapMode horizontalOverflow; public VerticalWrapMode verticalOverflow;
        public float lineSpacing; public bool resizeTextForBestFit; public int resizeTextMinSize, resizeTextMaxSize; public float preferredWidth, preferredHeight; public bool supportRichText;
    }
    public class Selectable : MonoBehaviour { public Graphic targetGraphic; public enum Transition { None, ColorTint } public Transition transition; public bool interactable; public ColorBlock colors; public Navigation navigation; }
    public struct ColorBlock { public Color normalColor, highlightedColor, pressedColor, selectedColor, disabledColor; public float colorMultiplier, fadeDuration; public static ColorBlock defaultColorBlock; }
    public struct Navigation { public enum Mode { None } public Mode mode; }
    public class Button : Selectable { public UnityEvent onClick = new UnityEvent(); }
    public class InputField : Selectable
    {
        public string text; public Text textComponent; public Graphic placeholder; public int characterLimit; public enum ContentType { Standard, Password, EmailAddress } public ContentType contentType; public UnityEvent<string> onValueChanged; public UnityEvent<string> onEndEdit;
        public enum LineType { SingleLine, MultiLineSubmit, MultiLineNewline } public LineType lineType; public void ForceLabelUpdate() { } public bool isFocused; public void ActivateInputField() { } public void DeactivateInputField() { } public int caretPosition; public Color caretColor, selectionColor; public bool customCaretColor;
    }
    public class ScrollRect : MonoBehaviour { public RectTransform content, viewport; public bool horizontal, vertical; public float scrollSensitivity; public Vector2 normalizedPosition; public enum MovementType { Clamped } public MovementType movementType; }
    public class LayoutElement : MonoBehaviour { public float preferredHeight, preferredWidth, minHeight, minWidth, flexibleHeight, flexibleWidth; }
    public class HorizontalOrVerticalLayoutGroup : MonoBehaviour { public float spacing; public RectOffset padding; public bool childControlWidth, childControlHeight, childForceExpandWidth, childForceExpandHeight; public TextAnchor childAlignment; }
    public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class ContentSizeFitter : MonoBehaviour { public enum FitMode { Unconstrained, PreferredSize } public FitMode verticalFit, horizontalFit; }
    public class CanvasScaler : MonoBehaviour { public enum ScaleMode { ScaleWithScreenSize } public ScaleMode uiScaleMode; public Vector2 referenceResolution; public float matchWidthOrHeight; }
    public class GraphicRaycaster : MonoBehaviour { }
    public class RectMask2D : MonoBehaviour { }
    public class Mask : MonoBehaviour { public bool showMaskGraphic; }
    public class Outline : MonoBehaviour { public Color effectColor; public Vector2 effectDistance; }
    public class Shadow : MonoBehaviour { public Color effectColor; public Vector2 effectDistance; }
}
namespace UnityEngine.InputSystem
{
    public class ButtonControl { public bool wasPressedThisFrame, wasReleasedThisFrame, isPressed; }
    public class Keyboard { public static Keyboard current; public ButtonControl escapeKey, spaceKey, enterKey, numpadEnterKey, tabKey; public ButtonControl this[Key k] => null; }
    public enum Key { A, W, S, D }
    public class Mouse { public static Mouse current; public ButtonControl leftButton; public Vector2Control position; public Vector2Control scroll; }
    public class Vector2Control { public Vector2 ReadValue() => default; }
}
namespace ARO.Multiplayer
{
    // stand-ins for the files that need Netcode / Unity Gaming Services (not available outside the Unity install)
    public sealed class NetworkVehicle : UnityEngine.MonoBehaviour { public static readonly ARO.NetCore.Roster Players = new ARO.NetCore.Roster(8); }
    public class SessionService
    {
        public const int MaxPlayers = 8; public ARO.NetCore.SessionState State; public string Message, Code; public bool IsHost;
        public event System.Action<ARO.NetCore.SessionState, string> StateChanged;
        public System.Threading.Tasks.Task<string> CreateAsync(string n) => null; public System.Threading.Tasks.Task<bool> JoinByCodeAsync(string c) => null; public System.Threading.Tasks.Task LeaveAsync() => null;
    }
}
namespace ARO.World
{
    public sealed class RenderingRig : UnityEngine.MonoBehaviour
    {
        public static ARO.NetCore.QualityTier Current; public ARO.NetCore.QualityTier Tier; public TimeOfDay timeOfDay; public WeatherSystem weather;
        public void Init(UnityEngine.Camera c, UnityEngine.Light l) { }
    }
}
