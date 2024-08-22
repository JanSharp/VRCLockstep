using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using UnityEditor;
using UdonSharpEditor;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Collections.ObjectModel;
using System;
using TMPro;

namespace JanSharp.Internal
{
    [InitializeOnLoad]
    public static class LockstepInfoUIEditor
    {
        static LockstepInfoUIEditor() => JanSharp.OnBuildUtil.RegisterType<LockstepInfoUI>(OnBuild, 2);

        private static bool OnBuild(LockstepInfoUI infoUI)
        {
            SerializedObject proxy = new SerializedObject(infoUI);
            Lockstep lockstep = (Lockstep)proxy.FindProperty("lockstep").objectReferenceValue;
            if (lockstep == null)
            {
                Debug.LogError("[Lockstep] The Lockstep Info UI requires an instance of the "
                    + "Lockstep prefab in the scene.", infoUI);
                return false;
            }

            {
                SerializedObject lockstepProxy = new SerializedObject(lockstep);
                lockstepProxy.FindProperty("infoUI").objectReferenceValue = infoUI;
                lockstepProxy.ApplyModifiedProperties();
            }

            return true;
        }
    }
}
