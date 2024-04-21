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

namespace JanSharp
{
    [InitializeOnLoad]
    public static class LockstepOnBuild
    {
        private static Lockstep lockstep = null;

        private static Dictionary<LockstepEventType, List<UdonSharpBehaviour>> allListeners = new Dictionary<LockstepEventType, List<UdonSharpBehaviour>>()
        {
            { LockstepEventType.OnInit, new List<UdonSharpBehaviour>() },
            { LockstepEventType.OnClientJoined, new List<UdonSharpBehaviour>() },
            { LockstepEventType.OnClientBeginCatchUp, new List<UdonSharpBehaviour>() },
            { LockstepEventType.OnClientCaughtUp, new List<UdonSharpBehaviour>() },
            { LockstepEventType.OnClientLeft, new List<UdonSharpBehaviour>() },
            { LockstepEventType.OnMasterChanged, new List<UdonSharpBehaviour>() },
            { LockstepEventType.OnTick, new List<UdonSharpBehaviour>() },
            { LockstepEventType.OnImportStart, new List<UdonSharpBehaviour>() },
            { LockstepEventType.OnImportedGameState, new List<UdonSharpBehaviour>() },
            { LockstepEventType.OnImportFinished, new List<UdonSharpBehaviour>() },
        };
        private static List<LockstepGameState> allGameStates = new List<LockstepGameState>();
        public static ReadOnlyCollection<LockstepGameState> AllGameStates => allGameStates.AsReadOnly();

        private static List<(UdonSharpBehaviour inst, string iaName)> allInputActions = new List<(UdonSharpBehaviour inst, string iaName)>();

        private static Dictionary<System.Type, TypeCache> cache = new Dictionary<System.Type, TypeCache>();
        private class TypeCache
        {
            public LockstepEventType events;
            public List<(string iaName, string fieldName)> inputActions = new List<(string iaName, string fieldName)>();
            public List<string> lockstepFieldNames = new List<string>();
        }

        private static readonly LockstepEventType[] allEventTypes = new LockstepEventType[] {
            LockstepEventType.OnInit,
            LockstepEventType.OnClientJoined,
            LockstepEventType.OnClientBeginCatchUp,
            LockstepEventType.OnClientCaughtUp,
            LockstepEventType.OnClientLeft,
            LockstepEventType.OnMasterChanged,
            LockstepEventType.OnTick,
            LockstepEventType.OnImportStart,
            LockstepEventType.OnImportedGameState,
            LockstepEventType.OnImportFinished,
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

            foreach (List<UdonSharpBehaviour> listeners in allListeners.Values)
                listeners.Clear();
            allGameStates.Clear();
            allInputActions.Clear();
            return true;
        }

        private static bool PostOnBuild(Lockstep lockstep)
        {
            SerializedObject lockstepProxy = new SerializedObject(lockstep);

            foreach (var kvp in allListeners)
            {
                EditorUtil.SetArrayProperty(
                    lockstepProxy.FindProperty($"o{kvp.Key.ToString().Substring(1)}Listeners"),
                    kvp.Value,
                    (p, v) => p.objectReferenceValue = v
                );
            }

            allGameStates = allGameStates
                .OrderByDescending(gs => gs.GameStateSupportsImportExport)
                .ThenBy(gs => gs.GameStateDisplayName)
                .ThenBy(gs => gs.GameStateInternalName)
                .ToList();
            EditorUtil.SetArrayProperty(
                lockstepProxy.FindProperty("allGameStates"),
                allGameStates,
                (p, v) => p.objectReferenceValue = v
            );

            EditorUtil.SetArrayProperty(
                lockstepProxy.FindProperty("inputActionHandlerInstances"),
                allInputActions.Select(ia => ia.inst).ToList(),
                (p, v) => p.objectReferenceValue = v
            );

            EditorUtil.SetArrayProperty(
                lockstepProxy.FindProperty("inputActionHandlerEventNames"),
                allInputActions.Select(ia => ia.iaName).ToList(),
                (p, v) => p.stringValue = v
            );

            lockstepProxy.ApplyModifiedProperties();
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
                    Debug.LogError($"The method name {method.Name} does not match the expected Lockstep "
                        + $"event name {attr.EventType.ToString()} for the {ubType.Name} script.", ub);
                    result = false;
                    return;
                }
                typeCache.events |= attr.EventType;
            }

            void CheckInputActionAttribute(MethodInfo method)
            {
                LockstepInputActionAttribute attr = method.GetCustomAttribute<LockstepInputActionAttribute>();
                if (attr == null)
                    return;
                typeCache.inputActions.Add((method.Name, attr.IdFieldName));
            }

            foreach (MethodInfo method in ubType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                CheckEventAttribute(method);
                CheckInputActionAttribute(method);
            }

            foreach (FieldInfo field in ubType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (field.FieldType == typeof(LockstepAPI))
                {
                    SerializedObject ubProxy = new SerializedObject(ub);
                    if (ubProxy.FindProperty(field.Name) != null)
                        typeCache.lockstepFieldNames.Add(field.Name);
                }
            }

            cached = typeCache;
            cache[ubType] = typeCache;
            return result;
        }

        private static bool EventListenersOnBuild(UdonSharpBehaviour ub)
        {
            if (!TryGetEventTypes(ub, out TypeCache cached))
                return false;

            foreach (LockstepEventType eventType in allEventTypes)
                if ((cached.events & eventType) != 0)
                    allListeners[eventType].Add(ub);

            SerializedObject ubProxy = null;

            if (cached.inputActions.Any())
            {
                ubProxy = ubProxy ?? new SerializedObject(ub);
                foreach (var ia in cached.inputActions)
                {
                    // uintValue is not a thing in 2019.4. It exists in 2022.1.
                    ubProxy.FindProperty(ia.fieldName).intValue = allInputActions.Count;
                    allInputActions.Add((ub, ia.iaName));
                }
            }

            if (cached.lockstepFieldNames.Any())
            {
                ubProxy = ubProxy ?? new SerializedObject(ub);
                foreach (string fieldName in cached.lockstepFieldNames)
                    ubProxy.FindProperty(fieldName).objectReferenceValue = lockstep;
            }

            ubProxy?.ApplyModifiedProperties();

            return true;
        }

        private static bool GameStatesOnBuild(LockstepGameState gameState)
        {
            allGameStates.Add(gameState);
            return true;
        }
    }
}
