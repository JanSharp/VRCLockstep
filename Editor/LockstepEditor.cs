using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEditor;
using UdonSharpEditor;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace JanSharp.Internal
{
    [InitializeOnLoad]
    public static class LockstepOnBuild
    {
        private static Lockstep lockstep = null;

        private static Dictionary<LockstepEventType, List<(int order, UdonSharpBehaviour ub)>> allListeners = new Dictionary<LockstepEventType, List<(int order, UdonSharpBehaviour ub)>>()
        {
            { LockstepEventType.OnInit, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnClientBeginCatchUp, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnPreClientJoined, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnClientJoined, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnClientCaughtUp, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnClientLeft, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnMasterClientChanged, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnLockstepTick, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnImportStart, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnImportedGameState, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnImportFinished, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnGameStatesToAutosaveChanged, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnAutosaveIntervalSecondsChanged, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnIsAutosavePausedChanged, new List<(int order, UdonSharpBehaviour ub)>() },
            { LockstepEventType.OnLockstepNotification, new List<(int order, UdonSharpBehaviour ub)>() },
        };
        private static List<LockstepGameState> allGameStates = new List<LockstepGameState>();
        public static ReadOnlyCollection<LockstepGameState> AllGameStates => allGameStates.AsReadOnly();

        private static List<(UdonSharpBehaviour inst, string iaName, bool timed)> allInputActions = new List<(UdonSharpBehaviour inst, string iaName, bool timed)>();

        private static Dictionary<System.Type, TypeCache> cache = new Dictionary<System.Type, TypeCache>();
        private class TypeCache
        {
            public Dictionary<LockstepEventType, int> eventOrderLut;
            public List<(string iaName, string fieldName, bool timed)> inputActions;
        }

        private static readonly LockstepEventType[] allEventTypes = new LockstepEventType[] {
            LockstepEventType.OnInit,
            LockstepEventType.OnClientBeginCatchUp,
            LockstepEventType.OnPreClientJoined,
            LockstepEventType.OnClientJoined,
            LockstepEventType.OnClientCaughtUp,
            LockstepEventType.OnClientLeft,
            LockstepEventType.OnMasterClientChanged,
            LockstepEventType.OnLockstepTick,
            LockstepEventType.OnImportStart,
            LockstepEventType.OnImportedGameState,
            LockstepEventType.OnImportFinished,
            LockstepEventType.OnGameStatesToAutosaveChanged,
            LockstepEventType.OnAutosaveIntervalSecondsChanged,
            LockstepEventType.OnIsAutosavePausedChanged,
            LockstepEventType.OnLockstepNotification,
        };

        static LockstepOnBuild()
        {
            JanSharp.OnBuildUtil.RegisterType<Lockstep>(PreOnBuild, -1);
            JanSharp.OnBuildUtil.RegisterType<UdonSharpBehaviour>(EventListenersOnBuild, 0);
            JanSharp.OnBuildUtil.RegisterType<LockstepGameState>(GameStatesOnBuild, 0);
            JanSharp.OnBuildUtil.RegisterType<Lockstep>(PostOnBuild, 1);
        }

        private static bool PreOnBuild(Lockstep lockstep)
        {
            if (LockstepOnBuild.lockstep != null && LockstepOnBuild.lockstep != lockstep)
            {
                Debug.LogError("[Lockstep] There must only be one instance "
                    + $"of the {nameof(Lockstep)} script in a scene.", lockstep);
                return false;
            }
            LockstepOnBuild.lockstep = lockstep;

            foreach (List<(int order, UdonSharpBehaviour ub)> listeners in allListeners.Values)
                listeners.Clear();
            allGameStates.Clear();
            allInputActions.Clear();
            return true;
        }

        private static bool PostOnBuild(Lockstep lockstep)
        {
            RecheckWorldName(new Lockstep[] { lockstep }, lockstep.gameObject.scene);

            SerializedObject lockstepSo = new SerializedObject(lockstep);

            foreach (var kvp in allListeners)
            {
                EditorUtil.SetArrayProperty(
                    lockstepSo.FindProperty($"o{kvp.Key.ToString()[1..]}Listeners"),
                    kvp.Value.OrderBy(v => v.order).ToList(),
                    (p, v) => p.objectReferenceValue = v.ub
                );
            }

            allGameStates = allGameStates
                .OrderByDescending(gs => gs.GameStateSupportsImportExport)
                .ThenBy(gs => gs.GameStateDisplayName)
                .ThenBy(gs => gs.GameStateInternalName)
                .ToList();
            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("allGameStates"),
                allGameStates,
                (p, v) => p.objectReferenceValue = v
            );

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("inputActionHandlerInstances"),
                allInputActions.Select(ia => ia.inst).ToList(),
                (p, v) => p.objectReferenceValue = v
            );

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("inputActionHandlerEventNames"),
                allInputActions.Select(ia => ia.iaName).ToList(),
                (p, v) => p.stringValue = v
            );

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("inputActionHandlersRequireTimeTracking"),
                allInputActions.Select(ia => ia.timed).ToList(),
                (p, v) => p.boolValue = v
            );

            lockstepSo.ApplyModifiedProperties();
            return true;
        }

        private static bool TryGetEventTypes(UdonSharpBehaviour ub, out TypeCache cached)
        {
            System.Type ubType = ub.GetType();
            if (cache.TryGetValue(ubType, out cached))
                return true;

            bool result = true;
            TypeCache typeCache = new TypeCache();

            void CheckEventAttribute(MethodInfo method)
            {
                LockstepEventAttribute attr = method.GetCustomAttribute<LockstepEventAttribute>();
                if (attr == null)
                    return;
                if (method.Name != attr.EventType.ToString())
                {
                    Debug.LogError($"[Lockstep] The method name {method.Name} does not match the expected "
                        + $"Lockstep event name {attr.EventType} for the {ubType.Name} script.", ub);
                    result = false;
                    return;
                }
                if (!method.IsPublic)
                {
                    Debug.LogError($"[Lockstep] The method {ubType.Name}.{method.Name} is marked as a Lockstep "
                        + $"event, however Lockstep event methods must be public.", ub);
                    result = false;
                    return;
                }
                typeCache.eventOrderLut ??= new Dictionary<LockstepEventType, int>();
                typeCache.eventOrderLut.Add(attr.EventType, attr.Order);
            }

            void CheckInputActionAttribute(MethodInfo method)
            {
                LockstepInputActionAttribute attr = method.GetCustomAttribute<LockstepInputActionAttribute>();
                if (attr == null)
                    return;
                if (!method.IsPublic)
                {
                    Debug.LogError($"[Lockstep] The method {ubType.Name}.{method.Name} is marked as an input "
                        + $"action, however input action methods must be public.", ub);
                    result = false;
                    return;
                }
                FieldInfo idField = ubType.GetField(attr.IdFieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                bool hasNonSerializedAttribute = idField?.GetCustomAttribute<System.NonSerializedAttribute>(inherit: true) != null;
                bool hasSerializeFieldAttribute = idField?.GetCustomAttribute<SerializeField>(inherit: true) != null;
                if (!(idField != null
                    && idField.FieldType == typeof(uint)
                    && ((idField.IsPublic && !hasNonSerializedAttribute)
                        || (!idField.IsPublic && hasSerializeFieldAttribute))
                    && !(hasNonSerializedAttribute && hasSerializeFieldAttribute)))
                {
                    Debug.LogError($"[Lockstep] The id field {ubType.Name}.{attr.IdFieldName} for the input "
                        + $"action {ubType.Name}.{method.Name} must be a non static (aka instance) field of "
                        + $"type uint. "
                        + $"If it is a public field it must not have the System.NonSerializedAttribute. "
                        + $"If it is a private field it must have the UnityEngine.SerializeField attribute. "
                        + $"It must not have both of the mentioned attributes at the same time. "
                        + $"It is recommended to use the UnityEngine.HideInInspector attribute, as the value "
                        + $"of the variable is set by Lockstep at play mode and build time.", ub);
                    result = false;
                    return;
                }
                typeCache.inputActions ??= new List<(string iaName, string fieldName, bool timed)>();
                typeCache.inputActions.Add((method.Name, attr.IdFieldName, attr.TrackTiming));
            }

            foreach (MethodInfo method in ubType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                CheckEventAttribute(method);
                CheckInputActionAttribute(method);
            }

            cached = typeCache;
            // Do not save it in the cache if it failed, otherwise subsequent runs of this logic without
            // assembly reloads in between would return true instead of logging an error and returning false
            // like they should.
            if (result)
                cache[ubType] = typeCache;
            return result;
        }

        private static bool EventListenersOnBuild(UdonSharpBehaviour ub)
        {
            if (!TryGetEventTypes(ub, out TypeCache cached))
                return false;

            if (cached.eventOrderLut != null)
                foreach (LockstepEventType eventType in allEventTypes)
                    if (cached.eventOrderLut.TryGetValue(eventType, out int order))
                        allListeners[eventType].Add((order, ub));

            if (cached.inputActions != null)
            {
                SerializedObject ubSo = new SerializedObject(ub);
                foreach (var ia in cached.inputActions)
                {
                    ubSo.FindProperty(ia.fieldName).uintValue = (uint)allInputActions.Count;
                    allInputActions.Add((ub, ia.iaName, ia.timed));
                }
                ubSo.ApplyModifiedProperties();
            }

            return true;
        }

        private static bool GameStatesOnBuild(LockstepGameState gameState)
        {
            allGameStates.Add(gameState);
            if (gameState.GameStateLowestSupportedDataVersion > gameState.GameStateDataVersion)
            {
                Debug.LogError($"[Lockstep] The GameStateLowestSupportedDataVersion "
                    + $"({gameState.GameStateLowestSupportedDataVersion}) must be less than or equal to the "
                    + $"GameStateDataVersion ({gameState.GameStateDataVersion}) for {gameState.GetType().Name}.",
                    gameState);
                return false;
            }
            return true;
        }

        public static void RecheckWorldName(Lockstep[] targets, Scene scene)
        {
            if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
                return;
            SerializedObject so = new SerializedObject(targets);
            so.FindProperty("worldName").stringValue = scene.name;
            so.ApplyModifiedProperties();
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(Lockstep))]
    public class LockstepEditor : Editor
    {
        private SerializedObject so;
        private SerializedProperty useSceneNameAsWorldNameProp;
        private SerializedProperty worldNameProp;

        private void OnEnable()
        {
            so = serializedObject;
            useSceneNameAsWorldNameProp = serializedObject.FindProperty("useSceneNameAsWorldName");
            worldNameProp = serializedObject.FindProperty("worldName");
            RecheckWorldName();
        }

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(targets))
                return;
            EditorGUILayout.Space();

            so.Update();
            EditorGUILayout.PropertyField(useSceneNameAsWorldNameProp);
            if (so.ApplyModifiedProperties())
                RecheckWorldName();
            using (new EditorGUI.DisabledGroupScope(useSceneNameAsWorldNameProp.boolValue))
                EditorGUILayout.PropertyField(worldNameProp);
            so.ApplyModifiedProperties();

            #if LockstepDebug
            EditorGUILayout.Space();
            GUILayout.Label("Debug", EditorStyles.boldLabel);
            DrawDefaultInspector();
            #endif
        }

        private void RecheckWorldName()
        {
            if (!useSceneNameAsWorldNameProp.boolValue)
                return;
            foreach (var group in targets.Cast<Lockstep>().GroupBy(l => l.gameObject.scene))
                LockstepOnBuild.RecheckWorldName(group.ToArray(), group.Key);
            so.Update();
        }
    }
}
