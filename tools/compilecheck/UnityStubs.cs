// -----------------------------------------------------------------------------
// Signature-only stubs for the UnityEngine API this project uses.
//
// These exist so Assets/Scripts can be compiled by a plain Roslyn build with no
// Unity installation. They implement NOTHING — every member is a declaration
// whose body is discarded. What this catches is real all the same: typos, wrong
// argument types, missing members, bad overrides, namespace mistakes and null
// hygiene, across both the code paths that a headless build would otherwise miss.
//
// WHAT IT DOES NOT CATCH, stated plainly so nobody trusts it too far:
//   * Unity's own IL post-processing (NGO generates RPC plumbing at build time).
//   * Anything about runtime behaviour, physics, rendering or replication.
//   * Whether the API shapes here actually match the shipped packages.
// It is a spellchecker, not a test suite.
// -----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public HideFlags hideFlags;
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
        public static void DontDestroyOnLoad(Object o) { }
        public static T FindObjectOfType<T>() where T : Object => default;
        public static T Instantiate<T>(T original) where T : Object => default;
        public static GameObject Instantiate(GameObject original) => default;
        public static GameObject Instantiate(GameObject original, Vector3 position, Quaternion rotation) => default;
        public static implicit operator bool(Object o) => false;
        public static bool operator ==(Object a, Object b) => false;
        public static bool operator !=(Object a, Object b) => false;
        public override bool Equals(object other) => false;
        public override int GetHashCode() => 0;
    }

    public enum HideFlags { None = 0 }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public static Vector2 one => default;
        public float magnitude => 0f;
        public float sqrMagnitude => 0f;
        public Vector2 normalized => default;
        public void Normalize() { }
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => default;
        public static Vector2 operator +(Vector2 a, Vector2 b) => default;
        public static Vector2 operator -(Vector2 a, Vector2 b) => default;
        public static Vector2 operator *(Vector2 a, float s) => default;
        public static Vector2 operator *(float s, Vector2 a) => default;
        public static Vector2 operator /(Vector2 a, float s) => default;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 one => default;
        public static Vector3 up => default;
        public static Vector3 down => default;
        public static Vector3 forward => default;
        public static Vector3 right => default;
        public float magnitude => 0f;
        public float sqrMagnitude => 0f;
        public Vector3 normalized => default;
        public static float Distance(Vector3 a, Vector3 b) => 0f;
        public static float Angle(Vector3 a, Vector3 b) => 0f;
        public static float Dot(Vector3 a, Vector3 b) => 0f;
        public static Vector3 Cross(Vector3 a, Vector3 b) => default;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => default;
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t) => default;
        public static Vector3 MoveTowards(Vector3 a, Vector3 b, float d) => default;
        public static Vector3 Scale(Vector3 a, Vector3 b) => default;
        public static Vector3 operator +(Vector3 a, Vector3 b) => default;
        public static Vector3 operator -(Vector3 a, Vector3 b) => default;
        public static Vector3 operator -(Vector3 a) => default;
        public static Vector3 operator *(Vector3 a, float s) => default;
        public static Vector3 operator *(float s, Vector3 a) => default;
        public static Vector3 operator /(Vector3 a, float s) => default;
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => default;
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Quaternion Euler(Vector3 e) => default;
        public static Quaternion AngleAxis(float angle, Vector3 axis) => default;
        public static Quaternion LookRotation(Vector3 forward) => default;
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) => default;
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => default;
        public static Quaternion Lerp(Quaternion a, Quaternion b, float t) => default;
        public static Quaternion operator *(Quaternion a, Quaternion b) => default;
        public static Vector3 operator *(Quaternion q, Vector3 v) => default;
        public static bool operator ==(Quaternion a, Quaternion b) => false;
        public static bool operator !=(Quaternion a, Quaternion b) => false;
        public override bool Equals(object other) => false;
        public override int GetHashCode() => 0;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => default;
        public static Color black => default;
        public static Color clear => default;
        public static Color operator *(Color c, float s) => default;
        public override int GetHashCode() => 0;
        public override bool Equals(object other) => false;
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;
        public const float Infinity = float.PositiveInfinity;
        public static float Abs(float v) => 0f;
        public static float Sign(float v) => 0f;
        public static float Min(float a, float b) => 0f;
        public static float Max(float a, float b) => 0f;
        public static int Min(int a, int b) => 0;
        public static int Max(int a, int b) => 0;
        public static float Clamp(float v, float lo, float hi) => 0f;
        public static int Clamp(int v, int lo, int hi) => 0;
        public static float Clamp01(float v) => 0f;
        public static float Lerp(float a, float b, float t) => 0f;
        public static float LerpAngle(float a, float b, float t) => 0f;
        public static float DeltaAngle(float a, float b) => 0f;
        public static float InverseLerp(float a, float b, float v) => 0f;
        public static float MoveTowards(float a, float b, float d) => 0f;
        public static float MoveTowardsAngle(float a, float b, float d) => 0f;
        public static float Sqrt(float v) => 0f;
        public static float Sin(float v) => 0f;
        public static float Cos(float v) => 0f;
        public static float Atan2(float y, float x) => 0f;
        public static float Exp(float v) => 0f;
        public static float Pow(float a, float b) => 0f;
        public static float Repeat(float t, float length) => 0f;
        public static float Round(float v) => 0f;
        public static int RoundToInt(float v) => 0;
        public static int FloorToInt(float v) => 0;
        public static int CeilToInt(float v) => 0;
        public static bool Approximately(float a, float b) => false;
    }

    public static class Random
    {
        public static float value => 0f;
        public static Vector3 insideUnitSphere => default;
        public static Vector2 insideUnitCircle => default;
        public static float Range(float min, float max) => 0f;
        public static int Range(int min, int max) => 0;
        public static void InitState(int seed) { }
    }

    public class Component : Object
    {
        public Transform transform => default;
        public GameObject gameObject => default;
        public string tag;
        public T GetComponent<T>() => default;
        public T GetComponentInChildren<T>() => default;
        public T GetComponentInParent<T>() => default;
        public T[] GetComponentsInChildren<T>() => default;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => default;
        public Component GetComponent(Type type) => default;
    }

    public class Behaviour : Component { public bool enabled; public bool isActiveAndEnabled => false; }

    public class Transform : Component, IEnumerable
    {
        public Vector3 position;
        public Vector3 localPosition;
        public Vector3 localScale;
        public Vector3 eulerAngles;
        public Vector3 localEulerAngles;
        public Quaternion rotation;
        public Quaternion localRotation;
        public Vector3 forward => default;
        public Vector3 up => default;
        public Vector3 right => default;
        public Transform parent;
        public int childCount => 0;
        public void SetParent(Transform parent) { }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public Transform GetChild(int index) => default;
        public Vector3 TransformPoint(Vector3 point) => default;
        public Vector3 InverseTransformPoint(Vector3 point) => default;
        public Vector3 TransformDirection(Vector3 direction) => default;
        public void LookAt(Vector3 target) { }
        public IEnumerator GetEnumerator() => default;
    }

    public sealed class RectTransform : Transform
    {
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 offsetMin;
        public Vector2 offsetMax;
        public Vector2 pivot;
        public Vector2 sizeDelta;
        public Vector2 anchoredPosition;
    }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public GameObject(string name, params Type[] components) { }
        public Transform transform => default;
        public int layer;
        public bool activeSelf => false;
        public bool activeInHierarchy => false;
        public string tag;
        public void SetActive(bool value) { }
        public T AddComponent<T>() where T : Component => default;
        public Component AddComponent(Type type) => default;
        public T GetComponent<T>() => default;
        public T[] GetComponentsInChildren<T>() => default;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => default;
    }

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) => default;
        public void StopCoroutine(Coroutine routine) { }
        public void StopAllCoroutines() { }
        public void Invoke(string method, float time) { }
        public void CancelInvoke() { }
    }

    public sealed class Coroutine : Object { }

    // ---- Attributes --------------------------------------------------------
    public class PropertyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class HideInInspector : Attribute { }
    public class HeaderAttribute : PropertyAttribute { public HeaderAttribute(string header) { } }
    public class TooltipAttribute : PropertyAttribute { public TooltipAttribute(string tooltip) { } }
    public class RangeAttribute : PropertyAttribute { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Class)] public class RequireComponent : Attribute { public RequireComponent(Type t) { } }
    [AttributeUsage(AttributeTargets.Class)] public class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, BeforeSplashScreen, SubsystemRegistration, AfterAssembliesLoaded }

    // ---- Rendering ---------------------------------------------------------
    public sealed class Shader : Object { public static Shader Find(string name) => default; }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public Shader shader;
        public Color color;
        public int renderQueue;
        public bool HasProperty(string name) => false;
        public void SetFloat(string name, float value) { }
        public void SetInt(string name, int value) { }
        public void SetColor(string name, Color value) { }
        public void EnableKeyword(string keyword) { }
        public void DisableKeyword(string keyword) { }
    }

    public class Texture : Object { public FilterMode filterMode; public TextureWrapMode wrapMode; }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp }
    public enum TextureFormat { RGBA32, ARGB32, RGB24 }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height) { }
        public Texture2D(int width, int height, TextureFormat format, bool mipChain) { }
        public void SetPixels32(Color32[] colors) { }
        public void Apply() { }
        public void Apply(bool updateMipmaps) { }
    }

    public sealed class Mesh : Object
    {
        public Rendering.IndexFormat indexFormat;
        public int subMeshCount;
        public Vector3[] vertices;
        public Vector3[] normals;
        public Vector2[] uv;
        public int[] triangles;
        public void SetVertices(List<Vector3> vertices) { }
        public void SetUVs(int channel, List<Vector2> uvs) { }
        public void SetTriangles(List<int> triangles, int submesh) { }
        public void SetTriangles(int[] triangles, int submesh) { }
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
        public void Clear() { }
    }

    public class Renderer : Component
    {
        public bool enabled;
        public Material material;
        public Material sharedMaterial;
        public Material[] materials;
        public Material[] sharedMaterials;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
    }

    public sealed class MeshRenderer : Renderer { }
    public sealed class MeshFilter : Component { public Mesh mesh; public Mesh sharedMesh; }

    public sealed class TextMesh : Component
    {
        public string text;
        public Font font;
        public float characterSize;
        public int fontSize;
        public TextAnchor anchor;
        public TextAlignment alignment;
        public Color color;
    }

    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum TextAlignment { Left, Center, Right }

    public class Font : Object { public Material material; }

    public static class Resources
    {
        public static T GetBuiltinResource<T>(string path) where T : Object => default;
        public static T Load<T>(string path) where T : Object => default;
    }

    public sealed class Camera : Behaviour
    {
        public static Camera main => default;
        public float nearClipPlane;
        public float farClipPlane;
        public float fieldOfView;
        public int depth;
        public Color backgroundColor;
        public CameraClearFlags clearFlags;
    }
    public enum CameraClearFlags { Skybox = 1, SolidColor = 2, Depth = 3, Nothing = 4 }

    public sealed class Light : Behaviour
    {
        public LightType type;
        public Color color;
        public float intensity;
        public float range;
        public float spotAngle;
        public LightShadows shadows;
        public float shadowStrength;
    }
    public enum LightType { Spot, Directional, Point, Area }
    public enum LightShadows { None, Hard, Soft }

    public static class RenderSettings
    {
        public static bool fog;
        public static FogMode fogMode;
        public static Color fogColor;
        public static float fogDensity;
        public static Material skybox;
        public static Light sun;
        public static Rendering.AmbientMode ambientMode;
        public static Color ambientSkyColor;
        public static Color ambientEquatorColor;
        public static Color ambientGroundColor;
    }
    public enum FogMode { Linear = 1, Exponential = 2, ExponentialSquared = 3 }

    public sealed class AudioListener : Behaviour { public static float volume; }
    public sealed class AudioClip : Object { }
    public sealed class AudioSource : Behaviour
    {
        public AudioClip clip; public bool loop; public float volume; public float pitch; public bool playOnAwake;
        public void Play() { } public void Stop() { } public void PlayOneShot(AudioClip clip) { }
    }

    // ---- Physics -----------------------------------------------------------
    public class Collider : Component { public bool isTrigger; public bool enabled; }
    public sealed class BoxCollider : Collider { public Vector3 center; public Vector3 size; }
    public sealed class SphereCollider : Collider { public Vector3 center; public float radius; }

    // ---- Input and platform ------------------------------------------------
    public static class Input
    {
        public static bool GetKey(KeyCode key) => false;
        public static bool GetKeyDown(KeyCode key) => false;
        public static bool GetKeyUp(KeyCode key) => false;
        public static bool GetMouseButton(int button) => false;
        public static bool GetMouseButtonDown(int button) => false;
        public static float GetAxis(string name) => 0f;
        public static float GetAxisRaw(string name) => 0f;
        public static Vector3 mousePosition => default;
    }

    public enum KeyCode
    {
        None = 0, Backspace, Tab, Return, Escape, Space,
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Alpha0, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,
        LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt, F1, F2, F3
    }

    public static class Cursor { public static CursorLockMode lockState; public static bool visible; }
    public enum CursorLockMode { None, Locked, Confined }

    public static class Time
    {
        public static float deltaTime => 0f;
        public static float fixedDeltaTime => 0f;
        public static float time => 0f;
        public static float unscaledTime => 0f;
        public static float timeSinceLevelLoad => 0f;
        public static float realtimeSinceStartup => 0f;
        public static float timeScale;
    }

    public static class Screen { public static int width => 0; public static int height => 0; public static bool fullScreen; }

    public static class Application
    {
        public static string version => string.Empty;
        public static bool isPlaying => false;
        public static int targetFrameRate;
        public static void Quit() { }
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
        public static void DrawLine(Vector3 a, Vector3 b, Color c) { }
    }

    public static class PlayerPrefs
    {
        public static string GetString(string key, string defaultValue) => defaultValue;
        public static float GetFloat(string key, float defaultValue) => defaultValue;
        public static int GetInt(string key, int defaultValue) => defaultValue;
        public static void SetString(string key, string value) { }
        public static void SetFloat(string key, float value) { }
        public static void SetInt(string key, int value) { }
        public static void Save() { }
    }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 }
    public enum IndexFormat { UInt16 = 0, UInt32 = 1 }
    public enum AmbientMode { Skybox = 0, Trilight = 1, Flat = 3, Custom = 4 }
    public enum BlendMode { Zero, One, DstColor, SrcColor, OneMinusDstColor, SrcAlpha, OneMinusSrcColor, DstAlpha, OneMinusDstAlpha, SrcAlphaSaturate, OneMinusSrcAlpha }
}

namespace UnityEngine.Events
{
    public class UnityEventBase { }
    public class UnityEvent : UnityEventBase
    {
        public void AddListener(Action call) { }
        public void RemoveListener(Action call) { }
        public void RemoveAllListeners() { }
        public void Invoke() { }
    }
    public class UnityEvent<T> : UnityEventBase
    {
        public void AddListener(Action<T> call) { }
        public void RemoveListener(Action<T> call) { }
        public void RemoveAllListeners() { }
        public void Invoke(T arg) { }
    }
}

namespace UnityEngine.EventSystems
{
    public class UIBehaviour : MonoBehaviour { }
    public class EventSystem : UIBehaviour { public static EventSystem current; public void SetSelectedGameObject(GameObject go) { } }
    public class BaseInputModule : UIBehaviour { }
    public class PointerInputModule : BaseInputModule { }
    public class StandaloneInputModule : PointerInputModule { }
}

namespace UnityEngine.UI
{
    using UnityEngine.Events;

    public class CanvasRenderer : Component { }

    public class Canvas : Behaviour
    {
        public RenderMode renderMode;
        public int sortingOrder;
        public Camera worldCamera;
    }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }

    public class CanvasScaler : Behaviour
    {
        public ScaleMode uiScaleMode;
        public Vector2 referenceResolution;
        public float matchWidthOrHeight;
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
    }

    public class GraphicRaycaster : Behaviour { }

    public class Graphic : UnityEngine.EventSystems.UIBehaviour
    {
        public Color color;
        public bool raycastTarget;
        public RectTransform rectTransform => default;
    }

    public class MaskableGraphic : Graphic { }

    public class Image : MaskableGraphic
    {
        public Sprite sprite;
        public Type type;
        public FillMethod fillMethod;
        public float fillAmount;
        public bool preserveAspect;
        public enum Type { Simple, Sliced, Tiled, Filled }
        public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
    }

    public class RawImage : MaskableGraphic { public Texture texture; }
    public class Sprite : Object { }

    public class Text : MaskableGraphic
    {
        public string text;
        public Font font;
        public int fontSize;
        public TextAnchor alignment;
        public bool supportRichText;
        public HorizontalWrapMode horizontalOverflow;
        public VerticalWrapMode verticalOverflow;
    }
    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }

    public struct ColorBlock
    {
        public Color normalColor, highlightedColor, pressedColor, selectedColor, disabledColor;
        public float colorMultiplier, fadeDuration;
    }

    public class Selectable : UnityEngine.EventSystems.UIBehaviour
    {
        public Graphic targetGraphic;
        public ColorBlock colors;
        public bool interactable;
    }

    public class Button : Selectable { public ButtonClickedEvent onClick = new ButtonClickedEvent(); public class ButtonClickedEvent : UnityEvent { } }

    public class Slider : Selectable
    {
        public RectTransform fillRect;
        public RectTransform handleRect;
        public Direction direction;
        public float minValue, maxValue, value;
        public SliderEvent onValueChanged = new SliderEvent();
        public enum Direction { LeftToRight, RightToLeft, BottomToTop, TopToBottom }
        public class SliderEvent : UnityEvent<float> { }
    }

    public class Toggle : Selectable
    {
        public bool isOn;
        public Graphic graphic;
        public ToggleEvent onValueChanged = new ToggleEvent();
        public class ToggleEvent : UnityEvent<bool> { }
    }

    public class InputField : Selectable
    {
        public string text;
        public Text textComponent;
        public Graphic placeholder;
        public int characterLimit;
        public ContentType contentType;
        public SubmitEvent onEndEdit = new SubmitEvent();
        public OnChangeEvent onValueChanged = new OnChangeEvent();
        public enum ContentType { Standard, IntegerNumber, DecimalNumber, Alphanumeric, Name, EmailAddress, Password, Pin, Custom }
        public class SubmitEvent : UnityEvent<string> { }
        public class OnChangeEvent : UnityEvent<string> { }
    }
}
