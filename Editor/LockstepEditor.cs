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
        private static string gameStateDependencyTreeErrorMessage = null;
        private static Dictionary<System.Type, GSTypeWithDeps> gsTypeWithDepsLut = null;

        private static List<LockstepGameState> allGameStates = new List<LockstepGameState>();
        public static ReadOnlyCollection<LockstepGameState> AllGameStates => allGameStates.AsReadOnly();

        private static List<(UdonSharpBehaviour inst, string iaName, bool timed)> allInputActions = new();
        private static List<(UdonSharpBehaviour inst, string listenerName, uint interval, int order)> allOnNthTickListeners = new();

        private static Dictionary<System.Type, TypeCache> cache = new();
        private class TypeCache
        {
            public List<(string iaName, string fieldName, bool timed)> inputActions;
            public List<(string eventName, uint interval, int order)> onNthTickListeners;
        }

        static LockstepOnBuild()
        {
            BuildDependencyTree();
            OnBuildUtil.RegisterAction(PrepareDependencyTreeOnBuild, -2);
            OnBuildUtil.RegisterType<Lockstep>(PreOnBuild, -1); // TODO: change this to use cumulative.
            OnBuildUtil.RegisterType<UdonSharpBehaviour>(InputActionsOnBuild, 0);
            OnBuildUtil.RegisterType<LockstepGameState>(GameStatesOnBuild, 0);
            OnBuildUtil.RegisterType<Lockstep>(PostOnBuild, 1);
        }

        private static void DependencyTreeError(string message)
        {
            gameStateDependencyTreeErrorMessage = message;
            Debug.LogError(message);
        }

        private static void BuildDependencyTree()
        {
            gameStateDependencyTreeErrorMessage = null;
            gsTypeWithDepsLut = null;

            var nonGSTypesWithDepAttr = OnAssemblyLoadUtil.AllUdonSharpBehaviourTypes
                .Where(t => t.IsDefined(typeof(LockstepGameStateDependencyAttribute), inherit: true)
                    && !EditorUtil.DerivesFrom(t, typeof(LockstepGameState)))
                .Select(t => t.Name)
                .ToList();
            if (nonGSTypesWithDepAttr.Any())
            {
                DependencyTreeError($"[Lockstep] The {nameof(LockstepGameStateDependencyAttribute)} can only "
                    + $"be applied to classes deriving from {nameof(LockstepGameState)}, however the attribute "
                    + $"is applied to: {string.Join(", ", nonGSTypesWithDepAttr)}");
                return;
            }

            List<GSTypeWithDeps> gameStateTypes = OnAssemblyLoadUtil.AllUdonSharpBehaviourTypes
                .Where(t => EditorUtil.DerivesFrom(t, typeof(LockstepGameState)))
                .Select(t => new GSTypeWithDeps(t))
                .ToList();
            gsTypeWithDepsLut = gameStateTypes.ToDictionary(t => t.gsType, t => t);
            foreach (GSTypeWithDeps type in gameStateTypes)
                if (!type.TryResolveGSTypes(gsTypeWithDepsLut))
                    return;
            foreach (GSTypeWithDeps type in gameStateTypes)
                if (!type.TryPopulateRecursiveDependencies())
                    return;
        }

        private class GSTypeWithDeps : System.IComparable<GSTypeWithDeps>
        {
            public System.Type gsType;
            public List<System.Type> rawDependencies;
            public List<GSTypeWithDeps> dependencies;
            public List<GSTypeWithDeps> recursiveDependenciesLut;

            public LockstepGameState instance;

            public GSTypeWithDeps(System.Type gsType)
            {
                this.gsType = gsType;
                rawDependencies = gsType.GetCustomAttributes<LockstepGameStateDependencyAttribute>(inherit: true)
                    .Select(a => a.GameStateType)
                    .ToList();
            }

            public bool TryResolveGSTypes(Dictionary<System.Type, GSTypeWithDeps> gsTypeWithDepsLut)
            {
                if (!ValidateRawDependencyTypes())
                    return false;
                dependencies = rawDependencies.Select(t => gsTypeWithDepsLut[t]).ToList();
                return true;
            }

            public bool ValidateRawDependencyTypes()
            {
                foreach (System.Type depType in rawDependencies)
                {
                    if (depType == null)
                    {
                        DependencyTreeError($"[Lockstep] The {gsType.Name} {nameof(LockstepGameState)} has a dependency on 'null'. "
                            + $"Use 'typeof()' as the argument for {nameof(LockstepGameStateDependencyAttribute)}.");
                        return false;
                    }
                    if (!EditorUtil.DerivesFrom(depType, typeof(LockstepGameState)))
                    {
                        DependencyTreeError($"[Lockstep] The {gsType.Name} {nameof(LockstepGameState)} has a dependency "
                            + $"on the type {depType.Name}, however said type does not derive from {nameof(LockstepGameState)}.");
                        return false;
                    }
                }
                return true;
            }

            public bool TryPopulateRecursiveDependencies()
            {
                recursiveDependenciesLut = new();
                foreach (GSTypeWithDeps dep in dependencies)
                    if (!Walk(dep))
                        return false;
                return true;
            }

            private bool Walk(GSTypeWithDeps toWalk)
            {
                if (toWalk == this)
                {
                    DependencyTreeError($"[Lockstep] GameState dependency loop detected for {gsType.Name}.");
                    return false;
                }
                if (recursiveDependenciesLut.Contains(toWalk))
                    return true;
                recursiveDependenciesLut.Add(toWalk);
                foreach (GSTypeWithDeps dep in dependencies)
                    if (!Walk(dep))
                        return false;
                return true;
            }

            public int CompareTo(GSTypeWithDeps other)
            {
                Debug.Log($"Comparing {gsType.Name} with {other.gsType.Name}!");
                if (instance == null || other.instance == null)
                    throw new System.Exception("Impossible.");
                if (recursiveDependenciesLut.Contains(other))
                    return 1;
                if (other.recursiveDependenciesLut.Contains(this))
                    return -1;
                int result = instance.GameStateDisplayName.ToLower().CompareTo(other.instance.GameStateDisplayName.ToLower());
                if (result != 0)
                    return result;
                return instance.GameStateInternalName.CompareTo(other.instance.GameStateInternalName);
            }
        }

        private static bool PrepareDependencyTreeOnBuild()
        {
            if (gameStateDependencyTreeErrorMessage != null)
            {
                Debug.LogError(gameStateDependencyTreeErrorMessage);
                return false;
            }
            foreach (GSTypeWithDeps type in gsTypeWithDepsLut.Values)
                type.instance = null;
            return true;
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

            allGameStates.Clear();
            allInputActions.Clear();
            allOnNthTickListeners.Clear();
            return true;
        }

        private static bool PostOnBuild(Lockstep lockstep)
        {
            RecheckWorldName(new Lockstep[] { lockstep }, lockstep.gameObject.scene);

            SerializedObject lockstepSo = new SerializedObject(lockstep);

            allGameStates = allGameStates
                .Select(gs => {
                    GSTypeWithDeps type = gsTypeWithDepsLut[gs.GetType()];
                    type.instance = gs;
                    return type;
                })
                .OrderBy(t => t)
                .Select(t => t.instance)
                .ToList();
            lockstepSo.FindProperty("allGameStatesCount").intValue = allGameStates.Count;
            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("allGameStates"),
                allGameStates,
                (p, v) => p.objectReferenceValue = v
            );

            List<LockstepGameState> gameStatesSupportingExport = allGameStates
                .Where(gs => gs.GameStateSupportsImportExport)
                .ToList();
            lockstepSo.FindProperty("gameStatesSupportingExportCount").intValue = gameStatesSupportingExport.Count;
            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("gameStatesSupportingExport"),
                gameStatesSupportingExport,
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


            allOnNthTickListeners = allOnNthTickListeners
                .OrderBy(l => l.interval)
                .ThenBy(l => l.order)
                .ThenBy(l => l.listenerName)
                .ToList();

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("onNthTickHandlerInstances"),
                allOnNthTickListeners.Select(l => l.inst).ToList(),
                (p, v) => p.objectReferenceValue = v
            );

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("onNthTickHandlerEventNames"),
                allOnNthTickListeners.Select(l => l.listenerName).ToList(),
                (p, v) => p.stringValue = v
            );

            var grouped = allOnNthTickListeners.GroupBy(l => l.interval).ToList();

            lockstepSo.FindProperty("onNthTickGroupsCount").intValue = grouped.Count;

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("onNthTickHandlerGroupSizes"),
                grouped.Select(g => g.Count()).ToList(),
                (p, v) => p.intValue = v
            );

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("onNthTickIntervals"),
                grouped.Select(g => g.Key).ToList(),
                (p, v) => p.uintValue = v
            );

            lockstepSo.ApplyModifiedProperties();
            return true;
        }

        private static bool TryGetTypeCache(UdonSharpBehaviour ub, out TypeCache cached)
        {
            System.Type ubType = ub.GetType();
            if (cache.TryGetValue(ubType, out cached))
                return true;

            bool result = true;
            TypeCache typeCache = new TypeCache();

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
                typeCache.inputActions ??= new();
                typeCache.inputActions.Add((method.Name, attr.IdFieldName, attr.TrackTiming));
            }

            void CheckOnNthTickAttribute(MethodInfo method)
            {
                LockstepOnNthTickAttribute attr = method.GetCustomAttribute<LockstepOnNthTickAttribute>();
                if (attr == null)
                    return;
                if (!method.IsPublic)
                {
                    Debug.LogError($"[Lockstep] The method {ubType.Name}.{method.Name} is marked as an "
                        + $"OnNthTick event listener, however such methods must be public.", ub);
                    result = false;
                    return;
                }
                if (attr.Interval == 0u)
                {
                    Debug.LogError($"[Lockstep] The method {ubType.Name}.{method.Name} is marked as an "
                        + $"OnNthTick event listener with an interval of 0. "
                        + $"The interval must be greater than 0.", ub);
                    result = false;
                    return;
                }
                typeCache.onNthTickListeners ??= new();
                typeCache.onNthTickListeners.Add((method.Name, attr.Interval, attr.Order));
            }

            foreach (MethodInfo method in ubType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                CheckInputActionAttribute(method);
                CheckOnNthTickAttribute(method);
            }

            cached = typeCache;
            // Do not save it in the cache if it failed, otherwise subsequent runs of this logic without
            // assembly reloads in between would return true instead of logging an error and returning false
            // like they should.
            if (result)
                cache[ubType] = typeCache;
            return result;
        }

        private static bool InputActionsOnBuild(UdonSharpBehaviour ub)
        {
            if (!TryGetTypeCache(ub, out TypeCache cached))
                return false;

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

            if (cached.onNthTickListeners != null)
                foreach (var listener in cached.onNthTickListeners)
                    allOnNthTickListeners.Add((ub, listener.eventName, listener.interval, listener.order));

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
