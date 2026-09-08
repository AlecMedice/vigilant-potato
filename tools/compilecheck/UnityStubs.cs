// Minimal API-shape stubs of UnityEngine / TextMeshPro / Netcode for GameObjects.
// Existence only: signatures mirror the real APIs so Roslyn type-checks the game scripts.
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object {
        public string name;
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
        public static T Instantiate<T>(T original) where T : Object => original;
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object => original;
    }
    public class Component : Object {
        public Transform transform { get; }
        public GameObject gameObject { get; }
        public T GetComponent<T>() => default;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => Array.Empty<T>();
        public T[] GetComponentsInChildren<T>() => Array.Empty<T>();
    }
    public class Behaviour : Component { public bool enabled { get; set; } }
    public class GameObject : Object {
        public Transform transform { get; }
        public bool activeSelf { get; }
        public void SetActive(bool v) { }
        public T GetComponent<T>() => default;
    }
    public class Transform : Component {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 forward { get; }
        public Vector3 right { get; }
        public Vector3 up { get; }
        public void Rotate(float x, float y, float z, Space s) { }
    }
    public enum Space { World, Self }
    public class Coroutine { }
    public class AsyncOperation { public bool isDone { get; } }
    public class MonoBehaviour : Behaviour {
        public Coroutine StartCoroutine(IEnumerator r) => null;
        public void StopCoroutine(Coroutine c) { }
        public void StopAllCoroutines() { }
    }
    public class Renderer : Component { public bool enabled { get; set; } }
    public class Camera : Behaviour { }
    public class AudioClip : Object { }
    public class AudioListener : Behaviour { public static float volume { get; set; } }
    public class AudioSource : Behaviour { public void PlayOneShot(AudioClip c) { } }
    public class CharacterController : Component { public void Move(Vector3 m) { } }
    public class CanvasGroup : Component {
        public float alpha { get; set; }
        public bool interactable { get; set; }
        public bool blocksRaycasts { get; set; }
    }

    public struct Vector2 {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public static Vector2 operator *(Vector2 a, float b) => default;
        public static Vector2 operator *(float b, Vector2 a) => default;
    }
    public struct Vector3 {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float magnitude => 0f;
        public float sqrMagnitude => 0f;
        public Vector3 normalized => default;
        public void Normalize() { }
        public static Vector3 zero => default;
        public static Vector3 up => default;
        public static Vector3 forward => default;
        public static float Distance(Vector3 a, Vector3 b) => 0f;
        public static Vector3 operator +(Vector3 a, Vector3 b) => default;
        public static Vector3 operator -(Vector3 a, Vector3 b) => default;
        public static Vector3 operator *(Vector3 a, float b) => default;
        public static Vector3 operator *(float b, Vector3 a) => default;
    }
    public struct Quaternion {
        public static Quaternion identity => default;
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Quaternion LookRotation(Vector3 f, Vector3 u) => default;
        public static Vector3 operator *(Quaternion q, Vector3 v) => default;
    }
    public struct Color {
        public Color(float r, float g, float b, float a) { }
        public static Color yellow => default;
        public static Color red => default;
    }

    public static class Mathf {
        public const float Deg2Rad = 0.0174f, Rad2Deg = 57.29f, PI = 3.14159f;
        public static float Clamp01(float v) => v;
        public static float Clamp(float v, float a, float b) => v;
        public static int Clamp(int v, int a, int b) => v;
        public static float Lerp(float a, float b, float t) => a;
        public static float MoveTowards(float a, float b, float d) => a;
        public static float Max(float a, float b) => a;
        public static float Min(float a, float b) => a;
        public static int Max(int a, int b) => a;
        public static float Abs(float a) => a;
        public static float Sqrt(float a) => a;
        public static float Sin(float a) => a;
        public static float Cos(float a) => a;
        public static float Atan2(float a, float b) => a;
        public static int RoundToInt(float a) => 0;
        public static int FloorToInt(float a) => 0;
        public static bool Approximately(float a, float b) => false;
    }
    public static class Time {
        public static float time { get; }
        public static float deltaTime { get; }
        public static float unscaledDeltaTime { get; }
        public static float realtimeSinceStartup { get; }
        public static float timeScale { get; set; }
    }
    public static class Debug {
        public static void Log(object m) { }
        public static void LogWarning(object m) { }
        public static void LogWarning(object m, Object ctx) { }
        public static void LogError(object m) { }
        public static void LogError(object m, Object ctx) { }
    }
    public static class Random {
        public static Vector2 insideUnitCircle => default;
        public static Vector3 insideUnitSphere => default;
    }
    public static class Gizmos {
        public static Color color { get; set; }
        public static void DrawWireSphere(Vector3 c, float r) { }
        public static void DrawLine(Vector3 a, Vector3 b) { }
    }
    public static class Application {
        public static string version => "";
        public static bool isPlaying => false;
        public static void Quit() { }
    }
    public static class Screen { public static bool fullScreen { get; set; } }
    public enum KeyCode { None, Space, Escape, W, A, S, D }
    public static class Input {
        public static float GetAxisRaw(string axis) => 0f;
        public static float GetAxis(string axis) => 0f;
        public static bool GetKeyDown(KeyCode k) => false;
        public static bool GetMouseButtonDown(int b) => false;
    }
    public static class Cursor {
        public static CursorLockMode lockState { get; set; }
        public static bool visible { get; set; }
    }
    public enum CursorLockMode { None, Locked, Confined }
    public static class PlayerPrefs {
        public static string GetString(string k, string d) => d;
        public static float GetFloat(string k, float d) => d;
        public static int GetInt(string k, int d) => d;
        public static void SetString(string k, string v) { }
        public static void SetFloat(string k, float v) { }
        public static void SetInt(string k, int v) { }
        public static void Save() { }
    }

    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class HeaderAttribute : PropertyAttribute { public HeaderAttribute(string h) { } }
    [AttributeUsage(AttributeTargets.Field)] public class TooltipAttribute : PropertyAttribute { public TooltipAttribute(string t) { } }
    [AttributeUsage(AttributeTargets.Field)] public class RangeAttribute : PropertyAttribute { public RangeAttribute(float a, float b) { } }
    public class PropertyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class RequireComponent : Attribute { public RequireComponent(Type t) { } }
    [AttributeUsage(AttributeTargets.Class)] public class DisallowMultipleComponent : Attribute { }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T>(T arg);
    public class UnityEvent { public void AddListener(UnityAction a) { } public void RemoveAllListeners() { } }
    public class UnityEvent<T> { public void AddListener(UnityAction<T> a) { } public void RemoveAllListeners() { } }
}

namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single, Additive }
    public struct Scene {
        public bool isLoaded => false;
        public bool IsValid() => false;
        public static bool operator ==(Scene a, Scene b) => false;
        public static bool operator !=(Scene a, Scene b) => false;
        public override bool Equals(object o) => false;
        public override int GetHashCode() => 0;
    }
    public static class SceneManager {
        public static Scene GetSceneByName(string n) => default;
        public static Scene GetSceneAt(int i) => default;
        public static bool SetActiveScene(Scene s) => false;
        public static AsyncOperation UnloadSceneAsync(Scene s) => null;
    }
}

namespace UnityEngine.AI
{
    public enum NavMeshPathStatus { PathComplete, PathPartial, PathInvalid }
    public class NavMeshPath { public NavMeshPathStatus status { get; } }
    public struct NavMeshHit { public Vector3 position => default; }
    public static class NavMesh {
        public const int AllAreas = -1;
        public static bool SamplePosition(Vector3 p, out NavMeshHit hit, float maxDistance, int areaMask) { hit = default; return false; }
    }
    public class NavMeshAgent : Behaviour {
        public float speed { get; set; }
        public float angularSpeed { get; set; }
        public float acceleration { get; set; }
        public float stoppingDistance { get; set; }
        public float baseOffset { get; set; }
        public bool autoBraking { get; set; }
        public bool updateRotation { get; set; }
        public int areaMask { get; set; }
        public bool isOnNavMesh => false;
        public bool pathPending => false;
        public bool hasPath => false;
        public float remainingDistance => 0f;
        public Vector3 velocity { get; set; }
        public Vector3 destination { get; set; }
        public NavMeshPathStatus pathStatus => default;
        public bool SetDestination(Vector3 t) => false;
        public bool SetPath(NavMeshPath p) => false;
        public bool CalculatePath(Vector3 t, NavMeshPath p) => false;
        public void ResetPath() { }
        public bool Warp(Vector3 p) => false;
    }
}

namespace UnityEngine.UI
{
    using UnityEngine.Events;
    public class Button : Component { public UnityEvent onClick { get; } }
    public class Slider : Component {
        public float minValue { get; set; }
        public float maxValue { get; set; }
        public float value { get; set; }
        public UnityEvent<float> onValueChanged { get; }
        public void SetValueWithoutNotify(float v) { }
    }
    public class Toggle : Component {
        public bool isOn { get; set; }
        public UnityEvent<bool> onValueChanged { get; }
        public void SetIsOnWithoutNotify(bool v) { }
    }
    public class Image : Component { public float fillAmount { get; set; } }
}

namespace UnityEngine.EventSystems
{
    public class EventSystem : Component {
        public static EventSystem current { get; }
        public void SetSelectedGameObject(GameObject go) { }
    }
}

namespace UnityEngine.InputSystem
{
    public class ButtonControl { public bool isPressed => false; public bool wasPressedThisFrame => false; }
    public class Vector2Control { public Vector2 ReadValue() => default; }
    public class Keyboard {
        public static Keyboard current { get; }
        public ButtonControl wKey { get; } public ButtonControl aKey { get; }
        public ButtonControl sKey { get; } public ButtonControl dKey { get; }
        public ButtonControl upArrowKey { get; } public ButtonControl downArrowKey { get; }
        public ButtonControl leftArrowKey { get; } public ButtonControl rightArrowKey { get; }
        public ButtonControl spaceKey { get; } public ButtonControl escapeKey { get; }
    }
    public class Mouse {
        public static Mouse current { get; }
        public Vector2Control delta { get; }
        public ButtonControl leftButton { get; }
    }
}

namespace TMPro
{
    using UnityEngine;
    using UnityEngine.Events;
    public class TMP_Text : Component { public string text { get; set; } }
    public class TMP_InputField : Component {
        public enum ContentType { Standard, IntegerNumber, DecimalNumber }
        public string text { get; set; }
        public int characterLimit { get; set; }
        public ContentType contentType { get; set; }
        public UnityEvent<string> onEndEdit { get; }
        public void SetTextWithoutNotify(string v) { }
    }
}

namespace Unity.Collections
{
    public struct FixedString32Bytes {
        // Padding only: the real type is an unmanaged fixed byte buffer, and NetworkVariable<T>
        // constrains T to unmanaged - so the stub has to be unmanaged too.
#pragma warning disable 414
        private long _a, _b, _c, _d;
#pragma warning restore 414
        public FixedString32Bytes(string s) { _a = _b = _c = _d = 0; }
        public override string ToString() => string.Empty;
    }
}
