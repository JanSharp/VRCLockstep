using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JanSharp.Internal
{
    [InitializeOnLoad]
    public static class LockstepOnBuild
    {
        private static string gameStateDependencyTreeErrorMessage = null;
        /// <summary>
        /// <para>Only <see langword="abstract"/> types may have multiple associated (deriving)
        /// <see cref="GSTypeWithDeps"/>. Non <see langword="abstract"/> types are always going to contain a
        /// single element in their list.</para>
        /// </summary>
        private static Dictionary<System.Type, List<GSTypeWithDeps>> gsTypeWithDepsLut = null;

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
            OnBuildUtil.RegisterAction(PreOnBuild, -1);
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
                .Where(t => !t.IsAbstract && EditorUtil.DerivesFrom(t, typeof(LockstepGameState)))
                .Select(t => new GSTypeWithDeps(t))
                .ToList();
            gsTypeWithDepsLut = gameStateTypes.ToDictionary(t => t.gsType, t => new List<GSTypeWithDeps>() { t });
            foreach (GSTypeWithDeps type in gameStateTypes)
                if (!type.AddAndValidateAbstractBaseClasses(gsTypeWithDepsLut))
                    return;
            foreach (GSTypeWithDeps type in gameStateTypes)
                if (!type.ValidateRawDependencyTypes(gsTypeWithDepsLut))
                    return;
            foreach (GSTypeWithDeps type in gameStateTypes)
                type.PopulateIncomingReverseDependencies(gsTypeWithDepsLut);
            foreach (GSTypeWithDeps type in gameStateTypes)
                type.PopulateResolvedDependencies(gsTypeWithDepsLut);
            foreach (GSTypeWithDeps type in gameStateTypes)
                if (!type.TryPopulateRecursiveDependencies())
                    return;
        }

        private class GSTypeWithDeps
        {
            public System.Type gsType;
            /// <summary>
            /// <para>Dependencies this game states has which it must load after.</para>
            /// </summary>
            public List<System.Type> rawDependencies = new();
            /// <summary>
            /// <para>Dependencies this game states has which it must load before.</para>
            /// </summary>
            public List<System.Type> rawReverseDependencies = new();
            /// <summary>
            /// <para>Dependencies other game states have which this game state must load after.</para>
            /// </summary>
            public List<GSTypeWithDeps> incomingReverseDependencies = new();
            public List<GSTypeWithDeps> dependencies;
            /// <summary>
            /// <para>Game states this game state must load after. Recursive in that if a dependency of this
            /// game state depends on another game state, that second game state is also be in this
            /// list.</para>
            /// </summary>
            public HashSet<GSTypeWithDeps> selfLoadsAfterRecursiveLut = new();
            /// <summary>
            /// <para>The exact same as <see cref="selfLoadsAfterRecursiveLut"/> except that this game state
            /// must load after, not before.</para>
            /// </summary>
            public HashSet<GSTypeWithDeps> selfLoadsBeforeRecursiveLut = new();

            public GSTypeWithDeps(System.Type gsType)
            {
                this.gsType = gsType;
                foreach (var attr in gsType.GetCustomAttributes<LockstepGameStateDependencyAttribute>(inherit: true))
                    if (attr.SelfLoadsBeforeDependency)
                        rawReverseDependencies.Add(attr.GameStateType);
                    else
                        rawDependencies.Add(attr.GameStateType);
            }

            public bool AddAndValidateAbstractBaseClasses(Dictionary<System.Type, List<GSTypeWithDeps>> gsTypeWithDepsLut)
            {
                System.Type baseType = gsType.BaseType;
                while (baseType != typeof(LockstepGameState))
                {
                    if (!baseType.IsAbstract)
                    {
                        DependencyTreeError($"[Lockstep] The {gsType.Name} {nameof(LockstepGameState)} inherits from the "
                            + $"(non abstract) {baseType.Name} {nameof(LockstepGameState)}, which is not supported. "
                            + $"Deriving from abstract classes is supported.");
                        return false;
                    }
                    if (gsTypeWithDepsLut.TryGetValue(baseType, out var list))
                        list.Add(this);
                    else
                        gsTypeWithDepsLut.Add(baseType, new() { this });
                    baseType = baseType.BaseType;
                }
                return true;
            }

            public bool ValidateRawDependencyTypes(Dictionary<System.Type, List<GSTypeWithDeps>> gsTypeWithDepsLut)
            {
                foreach (System.Type depType in rawDependencies)
                    if (!ValidateRawDependencyType(gsTypeWithDepsLut, depType))
                        return false;
                foreach (System.Type depType in rawReverseDependencies)
                    if (!ValidateRawDependencyType(gsTypeWithDepsLut, depType))
                        return false;
                return true;
            }

            private bool ValidateRawDependencyType(Dictionary<System.Type, List<GSTypeWithDeps>> gsTypeWithDepsLut, System.Type dependencyType)
            {
                if (dependencyType == null)
                {
                    DependencyTreeError($"[Lockstep] The {gsType.Name} {nameof(LockstepGameState)} has a dependency on 'null'. "
                        + $"Use 'typeof()' as the argument for {nameof(LockstepGameStateDependencyAttribute)}.");
                    return false;
                }
                if (!EditorUtil.DerivesFrom(dependencyType, typeof(LockstepGameState)))
                {
                    DependencyTreeError($"[Lockstep] The {gsType.Name} {nameof(LockstepGameState)} has a dependency "
                        + $"on the type {dependencyType.Name}, however said type does not derive from {nameof(LockstepGameState)}.");
                    return false;
                }
                if (dependencyType == typeof(LockstepGameState))
                {
                    DependencyTreeError($"[Lockstep] The {gsType.Name} {nameof(LockstepGameState)} has a dependency "
                        + $"on the {nameof(LockstepGameState)} class itself, which is nonsensical.");
                    return false;
                }
                if (dependencyType.IsAbstract && !gsTypeWithDepsLut.ContainsKey(dependencyType))
                {
                    DependencyTreeError($"[Lockstep] The {gsType.Name} {nameof(LockstepGameState)} has a dependency "
                        + $"on the abstract {dependencyType.Name} {nameof(LockstepGameState)} class, however there is "
                        + $"no class deriving from said abstract class - there is no implementation.");
                    return false;
                }
                return true;
            }

            public void PopulateIncomingReverseDependencies(Dictionary<System.Type, List<GSTypeWithDeps>> gsTypeWithDepsLut)
            {
                foreach (System.Type depType in rawReverseDependencies)
                    foreach (GSTypeWithDeps type in gsTypeWithDepsLut[depType])
                        type.incomingReverseDependencies.AddRange(gsTypeWithDepsLut[gsType]);
            }

            public void PopulateResolvedDependencies(Dictionary<System.Type, List<GSTypeWithDeps>> gsTypeWithDepsLut)
            {
                dependencies = rawDependencies.SelectMany(t => gsTypeWithDepsLut[t])
                    .Union(incomingReverseDependencies)
                    .Distinct()
                    .ToList();
            }

            public bool TryPopulateRecursiveDependencies()
            {
                foreach (GSTypeWithDeps dep in dependencies)
                    if (!Walk(dep))
                        return false;
                foreach (var dep in selfLoadsAfterRecursiveLut)
                    dep.selfLoadsBeforeRecursiveLut.Add(this);
                return true;
            }

            private bool Walk(GSTypeWithDeps toWalk)
            {
                if (toWalk == this)
                {
                    DependencyTreeError($"[Lockstep] GameState dependency loop detected for {gsType.Name}.");
                    return false;
                }
                if (selfLoadsAfterRecursiveLut.Contains(toWalk))
                    return true;
                selfLoadsAfterRecursiveLut.Add(toWalk);
                foreach (GSTypeWithDeps dep in toWalk.dependencies)
                    if (!Walk(dep))
                        return false;
                return true;
            }
        }

        private class GSTypeWithDepsInstance : System.IComparable<GSTypeWithDepsInstance>
        {
            public GSTypeWithDeps typeWithDeps;
            public LockstepGameState instance;

            public GSTypeWithDepsInstance(GSTypeWithDeps typeWithDeps, LockstepGameState instance)
            {
                this.typeWithDeps = typeWithDeps;
                this.instance = instance;
            }

            public int CompareTo(GSTypeWithDepsInstance other)
            {
                if (instance == null || other.instance == null)
                    throw new System.Exception("Impossible.");
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
            return true;
        }

        private static bool PreOnBuild()
        {
            allGameStates.Clear();
            allInputActions.Clear();
            allOnNthTickListeners.Clear();
            return true;
        }

        private static List<GSTypeWithDepsInstance> GetAllGameStatesInLoadOrder()
        {
            List<GSTypeWithDepsInstance> allGSInLoadOrder = new(allGameStates.Count);
            foreach (GSTypeWithDepsInstance gs in allGameStates
                .Select(gs => new GSTypeWithDepsInstance(gsTypeWithDepsLut[gs.GetType()].Single(), gs))
                .OrderBy(t => t)) // Order alphabetically and deterministically.
            {
                HashSet<GSTypeWithDeps> loadsBeforeLut = gs.typeWithDeps.selfLoadsBeforeRecursiveLut;
                if (loadsBeforeLut.Count == 0)
                {
                    allGSInLoadOrder.Add(gs);
                    continue;
                }
                int targetIndex = allGSInLoadOrder.Count - 1;
                for (int j = targetIndex - 1; j >= 0; j--)
                    if (loadsBeforeLut.Contains(allGSInLoadOrder[j].typeWithDeps))
                        targetIndex = j;
                allGSInLoadOrder.Insert(targetIndex, gs);
            }
            return allGSInLoadOrder;
        }

        private static bool PostOnBuild(Lockstep lockstep)
        {
            RecheckWorldName(new Lockstep[] { lockstep }, lockstep.gameObject.scene);

            SerializedObject lockstepSo = new SerializedObject(lockstep);

            List<GSTypeWithDepsInstance> allGSInLoadOrder = GetAllGameStatesInLoadOrder();

            lockstepSo.FindProperty("allGameStatesCount").intValue = allGSInLoadOrder.Count;
            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("allGameStates"),
                allGSInLoadOrder,
                (p, v) => p.objectReferenceValue = v.instance);

            List<GSTypeWithDepsInstance> gameStatesSupportingImportExport = allGSInLoadOrder
                .Where(gs => gs.instance.GameStateSupportsImportExport)
                .ToList();
            lockstepSo.FindProperty("gameStatesSupportingImportExportCount").intValue = gameStatesSupportingImportExport.Count;
            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("gameStatesSupportingImportExport"),
                gameStatesSupportingImportExport,
                (p, v) => p.objectReferenceValue = v.instance);

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("inputActionHandlerInstances"),
                allInputActions,
                (p, v) => p.objectReferenceValue = v.inst);

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("inputActionHandlerEventNames"),
                allInputActions,
                (p, v) => p.stringValue = v.iaName);

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("inputActionHandlersRequireTimeTracking"),
                allInputActions,
                (p, v) => p.boolValue = v.timed);


            allOnNthTickListeners = allOnNthTickListeners
                .OrderBy(l => l.interval)
                .ThenBy(l => l.order)
                .ThenBy(l => l.listenerName)
                .ToList();

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("onNthTickHandlerInstances"),
                allOnNthTickListeners,
                (p, v) => p.objectReferenceValue = v.inst);

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("onNthTickHandlerEventNames"),
                allOnNthTickListeners,
                (p, v) => p.stringValue = v.listenerName);

            var grouped = allOnNthTickListeners.GroupBy(l => l.interval).ToList();

            lockstepSo.FindProperty("onNthTickGroupsCount").intValue = grouped.Count;

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("onNthTickHandlerGroupSizes"),
                grouped,
                (p, v) => p.intValue = v.Count());

            EditorUtil.SetArrayProperty(
                lockstepSo.FindProperty("onNthTickIntervals"),
                grouped,
                (p, v) => p.uintValue = v.Key);

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
                FieldInfo idField = EditorUtil.GetFieldIncludingBase(ubType, attr.IdFieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                bool hasNonSerializedAttribute = idField?.GetCustomAttributes<System.NonSerializedAttribute>(inherit: true).Any() ?? false;
                bool hasSerializeFieldAttribute = idField?.GetCustomAttributes<SerializeField>(inherit: true).Any() ?? false;
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
            if (allGameStates.Any(gs => gs.GameStateInternalName == gameState.GameStateInternalName))
            {
                Debug.LogError($"[Lockstep] Multiple game states are attempting to use the "
                    + $"internal name '{gameState.GameStateInternalName}'.", gameState);
                return false;
            }
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

#if LOCKSTEP_DEBUG
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
