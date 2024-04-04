using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEditor;
using UdonSharpEditor;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Collections.ObjectModel;
using System;

namespace JanSharp
{
    [InitializeOnLoad]
    public static class LockStepGameStatesUIEditorOnBuild
    {
        private static ReadOnlyCollection<LockStepGameState> AllGameStates;

        static LockStepGameStatesUIEditorOnBuild() => JanSharp.OnBuildUtil.RegisterType<LockStepGameStatesUI>(OnBuild, 2);

        private static bool OnBuild(LockStepGameStatesUI gameStatesUI)
        {
            SerializedObject proxy = new SerializedObject(gameStatesUI);
            LockStep lockStep = (LockStep)proxy.FindProperty("lockStep").objectReferenceValue;
            if (lockStep == null)
            {
                Debug.LogError("[LockStep] The Lock Step Game State UI requires an instance of the "
                    + "Lock Step script in the scene.", gameStatesUI);
                return false;
            }

            AllGameStates = LockStepOnBuild.AllGameStates;

            GameObject mainGSEntryPrefab = (GameObject)proxy.FindProperty("mainGSEntryPrefab").objectReferenceValue;
            GameObject importGSEntryPrefab = (GameObject)proxy.FindProperty("importGSEntryPrefab").objectReferenceValue;
            GameObject exportGSEntryPrefab = (GameObject)proxy.FindProperty("exportGSEntryPrefab").objectReferenceValue;
            Transform mainGSList = (Transform)proxy.FindProperty("mainGSList").objectReferenceValue;
            Transform importGSList = (Transform)proxy.FindProperty("importGSList").objectReferenceValue;
            Transform exportGSList = (Transform)proxy.FindProperty("exportGSList").objectReferenceValue;
            if (mainGSEntryPrefab == null || importGSEntryPrefab == null || exportGSEntryPrefab == null
                || mainGSList == null || importGSList == null || exportGSList == null)
            {
                Debug.LogError("[LockStep] The Lock Step Game State UI is missing internal references.");
                return false;
            }

            PopulateList<LockStepMainGSEntry>(proxy, "mainGSEntries", " (MainGSEntry)", mainGSList, mainGSEntryPrefab, null);
            PopulateList<LockStepImportGSEntry>(proxy, "importGSEntries", " (ImportGSEntry)", importGSList, importGSEntryPrefab, null);
            PopulateList<LockStepExportGSEntry>(proxy, "exportGSEntries", " (ExportGSEntry)", exportGSList, exportGSEntryPrefab, (inst, gs) => {
                SerializedObject instProxy = new SerializedObject(inst);
                instProxy.FindProperty("gameStatesUI").objectReferenceValue = gameStatesUI;
                instProxy.ApplyModifiedPropertiesWithoutUndo();
            });

            proxy.ApplyModifiedPropertiesWithoutUndo();

            return true;
        }

        private static void PopulateList<T>(
            SerializedObject proxy,
            string entriesArrayName,
            string postfix,
            Transform list,
            GameObject prefab,
            Action<T, LockStepGameState> callback)
            where T : LockStepGameStateEntryBase
        {
            T[] entries = new T[AllGameStates.Count];
            for (int i = 0; i < AllGameStates.Count; i++)
            {
                GameObject inst;
                if (i < list.childCount)
                    inst = list.GetChild(i).gameObject;
                else
                {
                    inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    inst.transform.SetParent(list, false);
                }
                inst.name = AllGameStates[i].GameStateInternalName + postfix;
                entries[i] = inst.GetComponent<T>();
                entries[i].displayNameText.text = AllGameStates[i].GameStateDisplayName;
                if (callback != null)
                    callback(entries[i], AllGameStates[i]);
            }

            while (list.childCount > AllGameStates.Count)
                GameObject.DestroyImmediate(list.GetChild(list.childCount - 1));

            EditorUtil.SetArrayProperty(proxy.FindProperty(entriesArrayName), entries, (p, v) => p.objectReferenceValue = v);
        }
    }
}
