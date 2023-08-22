using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEditor;
using UdonSharpEditor;
using System.Collections.Generic;
using System.Reflection;

namespace JanSharp
{
    [InitializeOnLoad]
    public static class LockStepOnBuild
    {
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

        private static Dictionary<System.Type, LockStepEventType> cache = new Dictionary<System.Type, LockStepEventType>();

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
            foreach (List<UdonSharpBehaviour> listeners in allListeners.Values)
                listeners.Clear();
            allGameStates.Clear();
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

            lockStepProxy.ApplyModifiedProperties();
            return true;
        }

        private static bool TryGetEventTypes(UdonSharpBehaviour ub, out LockStepEventType events)
        {
            System.Type ubType = ub.GetType();
            if (cache.TryGetValue(ubType, out events))
                return true;

            bool result = true;
            events = 0;

            foreach (MethodInfo method in ubType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                LockStepEventAttribute attr = method.GetCustomAttribute<LockStepEventAttribute>();
                if (attr == null)
                    continue;
                if (method.Name != attr.EventType.ToString())
                {
                    Debug.LogError($"The method name {method.Name} does not match the expected lock step "
                        + $"event name {attr.EventType.ToString()} for the {ubType.Name} script.", ub);
                    result = false;
                    continue;
                }
                events |= attr.EventType;
            }

            cache[ubType] = events;
            return result;
        }

        private static bool EventListenersOnBuild(UdonSharpBehaviour ub)
        {
            if (TryGetEventTypes(ub, out LockStepEventType events))
            {
                foreach (LockStepEventType eventType in allEventTypes)
                    if ((events & eventType) != 0)
                        allListeners[eventType].Add(ub);
                return true;
            }
            return false;
        }

        private static bool GameStatesOnBuild(LockStepGameState gameState)
        {
            allGameStates.Add(gameState);
            return true;
        }
    }
}
