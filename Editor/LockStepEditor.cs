using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEditor;
using UdonSharpEditor;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace JanSharp
{
    [InitializeOnLoad]
    public static class LockStepOnBuild
    {
        private static LockStep lockStep = null;

        private static Dictionary<LockStepEventType, List<UdonSharpBehaviour>> allListeners = new Dictionary<LockStepEventType, List<UdonSharpBehaviour>>()
        {
            { LockStepEventType.OnInit, new List<UdonSharpBehaviour>() },
            { LockStepEventType.OnClientJoined, new List<UdonSharpBehaviour>() },
            { LockStepEventType.OnClientBeginCatchUp, new List<UdonSharpBehaviour>() },
            { LockStepEventType.OnClientCaughtUp, new List<UdonSharpBehaviour>() },
            { LockStepEventType.OnClientLeft, new List<UdonSharpBehaviour>() },
            { LockStepEventType.OnTick, new List<UdonSharpBehaviour>() },
        };
        private static List<LockStepGameState> allGameStates = new List<LockStepGameState>();

        private static List<(UdonSharpBehaviour inst, string iaName)> allInputActions = new List<(UdonSharpBehaviour inst, string iaName)>();

        private static Dictionary<System.Type, TypeCache> cache = new Dictionary<System.Type, TypeCache>();
        private class TypeCache
        {
            public LockStepEventType events;
            public List<(string iaName, string fieldName)> inputActions = new List<(string iaName, string fieldName)>();
            public List<string> lockStepFieldNames = new List<string>();
        }

        private static readonly LockStepEventType[] allEventTypes = new LockStepEventType[] {
            LockStepEventType.OnInit,
            LockStepEventType.OnClientJoined,
            LockStepEventType.OnClientBeginCatchUp,
            LockStepEventType.OnClientCaughtUp,
            LockStepEventType.OnClientLeft,
            LockStepEventType.OnTick,
        };

        static LockStepOnBuild()
        {
            JanSharp.OnBuildUtil.RegisterType<LockStep>(PreOnBuild, -1);
            JanSharp.OnBuildUtil.RegisterType<UdonSharpBehaviour>(EventListenersOnBuild, 0);
            JanSharp.OnBuildUtil.RegisterType<LockStepGameState>(GameStatesOnBuild, 0);
            JanSharp.OnBuildUtil.RegisterType<LockStep>(PostOnBuild, 1);
        }

        private static bool PreOnBuild(LockStep lockStep)
        {
            if (LockStepOnBuild.lockStep != null && LockStepOnBuild.lockStep != lockStep)
            {
                Debug.LogError("[LockStep] There must only be one instance "
                    + $"of the {nameof(LockStep)} script in a scene.", lockStep);
                return false;
            }
            LockStepOnBuild.lockStep = lockStep;

            foreach (List<UdonSharpBehaviour> listeners in allListeners.Values)
                listeners.Clear();
            allGameStates.Clear();
            allInputActions.Clear();
            return true;
        }

        private static bool PostOnBuild(LockStep lockStep)
        {
            SerializedObject lockStepProxy = new SerializedObject(lockStep);

            foreach (var kvp in allListeners)
            {
                EditorUtil.SetArrayProperty(
                    lockStepProxy.FindProperty($"o{kvp.Key.ToString().Substring(1)}Listeners"),
                    kvp.Value,
                    (p, v) => p.objectReferenceValue = v
                );
            }

            EditorUtil.SetArrayProperty(
                lockStepProxy.FindProperty("allGameStates"),
                allGameStates,
                (p, v) => p.objectReferenceValue = v
            );

            EditorUtil.SetArrayProperty(
                lockStepProxy.FindProperty("inputActionHandlerInstances"),
                allInputActions.Select(ia => ia.inst).ToList(),
                (p, v) => p.objectReferenceValue = v
            );

            EditorUtil.SetArrayProperty(
                lockStepProxy.FindProperty("inputActionHandlerEventNames"),
                allInputActions.Select(ia => ia.iaName).ToList(),
                (p, v) => p.stringValue = v
            );

            lockStepProxy.ApplyModifiedProperties();
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
                LockStepEventAttribute attr = method.GetCustomAttribute<LockStepEventAttribute>();
                if (attr == null)
                    return;
                if (method.Name != attr.EventType.ToString())
                {
                    Debug.LogError($"The method name {method.Name} does not match the expected lock step "
                        + $"event name {attr.EventType.ToString()} for the {ubType.Name} script.", ub);
                    result = false;
                    return;
                }
                typeCache.events |= attr.EventType;
            }

            void CheckInputActionAttribute(MethodInfo method)
            {
                LockStepInputActionAttribute attr = method.GetCustomAttribute<LockStepInputActionAttribute>();
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
                if (field.FieldType == typeof(LockStep))
                {
                    SerializedObject ubProxy = new SerializedObject(ub);
                    if (ubProxy.FindProperty(field.Name) != null)
                        typeCache.lockStepFieldNames.Add(field.Name);
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

            foreach (LockStepEventType eventType in allEventTypes)
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

            if (cached.lockStepFieldNames.Any())
            {
                ubProxy = ubProxy ?? new SerializedObject(ub);
                foreach (string fieldName in cached.lockStepFieldNames)
                    ubProxy.FindProperty(fieldName).objectReferenceValue = lockStep;
            }

            ubProxy?.ApplyModifiedProperties();

            return true;
        }

        private static bool GameStatesOnBuild(LockStepGameState gameState)
        {
            allGameStates.Add(gameState);
            return true;
        }
    }
}
