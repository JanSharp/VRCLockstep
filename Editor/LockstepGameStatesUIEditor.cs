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
    public static class LockstepGameStatesUIEditorOnBuild
    {
        static LockstepGameStatesUIEditorOnBuild() => JanSharp.OnBuildUtil.RegisterType<LockstepGameStatesUI>(OnBuild, 2);

        private static bool OnBuild(LockstepGameStatesUI gameStatesUI)
        {
            SerializedObject proxy = new SerializedObject(gameStatesUI);
            Lockstep lockstep = (Lockstep)proxy.FindProperty("lockstep").objectReferenceValue;
            if (lockstep == null)
            {
                Debug.LogError("[Lockstep] The Lockstep Game State UI requires an instance of the "
                    + "Lockstep prefab in the scene.", gameStatesUI);
                return false;
            }

            GameObject mainGSEntryPrefab = (GameObject)proxy.FindProperty("mainGSEntryPrefab").objectReferenceValue;
            GameObject importGSEntryPrefab = (GameObject)proxy.FindProperty("importGSEntryPrefab").objectReferenceValue;
            GameObject exportGSEntryPrefab = (GameObject)proxy.FindProperty("exportGSEntryPrefab").objectReferenceValue;
            Transform mainGSList = (Transform)proxy.FindProperty("mainGSList").objectReferenceValue;
            Transform importGSList = (Transform)proxy.FindProperty("importGSList").objectReferenceValue;
            Transform exportGSList = (Transform)proxy.FindProperty("exportGSList").objectReferenceValue;
            TextMeshProUGUI exportSelectedText = (TextMeshProUGUI)proxy.FindProperty("exportSelectedText").objectReferenceValue;
            Button confirmExportButton = (Button)proxy.FindProperty("confirmExportButton").objectReferenceValue;
            if (mainGSEntryPrefab == null || importGSEntryPrefab == null || exportGSEntryPrefab == null
                || mainGSList == null || importGSList == null || exportGSList == null
                || exportSelectedText == null || confirmExportButton == null)
            {
                Debug.LogError("[Lockstep] The Lockstep Game State UI is missing internal references.", gameStatesUI);
                return false;
            }

            var allGameStates = LockstepOnBuild.AllGameStates;
            PopulateList<LockstepMainGSEntry>(
                allGameStates: allGameStates,
                proxy: proxy,
                entriesArrayName: "mainGSEntries",
                postfix: " (MainGSEntry)",
                list: mainGSList,
                prefab: mainGSEntryPrefab);
            PopulateList<LockstepImportGSEntry>(
                allGameStates: allGameStates,
                proxy: proxy,
                entriesArrayName: "importGSEntries",
                postfix: " (ImportGSEntry)",
                list: importGSList,
                prefab: importGSEntryPrefab,
                leaveInteractableUnchanged: true,
                callback: (inst, gs) => {
                    SerializedObject instProxy = new SerializedObject(inst);
                    instProxy.FindProperty("gameStatesUI").objectReferenceValue = gameStatesUI;
                    instProxy.FindProperty("gameState").objectReferenceValue = gs;
                    instProxy.ApplyModifiedProperties();

                    SerializedObject infoLabelProxy = new SerializedObject(inst.infoLabel);
                    infoLabelProxy.FindProperty("m_text").stringValue = gs.GameStateSupportsImportExport
                        ? ""
                        : "does not support import/export";
                    infoLabelProxy.ApplyModifiedProperties();
                });
            PopulateList<LockstepExportGSEntry>(
                allGameStates: allGameStates,
                proxy: proxy,
                entriesArrayName: "exportGSEntries",
                postfix: " (ExportGSEntry)",
                list: exportGSList,
                prefab: exportGSEntryPrefab,
                leaveIsOnUnchanged: true,
                callback: (inst, gs) => {
                    SerializedObject instProxy = new SerializedObject(inst);
                    instProxy.FindProperty("gameStatesUI").objectReferenceValue = gameStatesUI;
                    instProxy.FindProperty("gameState").objectReferenceValue = gs;
                    instProxy.ApplyModifiedProperties();

                    SerializedObject infoLabelProxy = new SerializedObject(inst.infoLabel);
                    infoLabelProxy.FindProperty("m_text").stringValue = gs.GameStateSupportsImportExport
                        ? "autosave"
                        : "does not support import/export";
                    infoLabelProxy.ApplyModifiedProperties();

                    SerializedObject infoObjProxy = new SerializedObject(inst.infoLabel.gameObject);
                    infoObjProxy.FindProperty("m_IsActive").boolValue = !gs.GameStateSupportsImportExport;
                    infoObjProxy.ApplyModifiedProperties();
                });

            int supportedCount = allGameStates.Count(gs => gs.GameStateSupportsImportExport);
            proxy.FindProperty("exportSelectedCount").intValue = supportedCount;
            SerializedObject exportSelectedTextProxy = new SerializedObject(exportSelectedText);
            exportSelectedTextProxy.FindProperty("m_text").stringValue = $"selected: {supportedCount}";
            exportSelectedTextProxy.ApplyModifiedProperties();
            SerializedObject confirmExportButtonProxy = new SerializedObject(confirmExportButton);
            confirmExportButtonProxy.FindProperty("m_Interactable").boolValue = supportedCount != 0;
            confirmExportButtonProxy.ApplyModifiedProperties();

            Slider autosaveIntervalSlider = (Slider)proxy.FindProperty("autosaveIntervalSlider").objectReferenceValue;
            float defaultAutosaveInterval = autosaveIntervalSlider.value;
            proxy.FindProperty("minAutosaveInterval").floatValue = autosaveIntervalSlider.minValue;
            proxy.FindProperty("defaultAutosaveInterval").floatValue = defaultAutosaveInterval;
            proxy.FindProperty("autosaveInterval").floatValue = defaultAutosaveInterval;
            proxy.ApplyModifiedProperties();

            TMP_InputField autosaveIntervalField = (TMP_InputField)proxy.FindProperty("autosaveIntervalField").objectReferenceValue;
            SerializedObject fieldProxy = new SerializedObject(autosaveIntervalField);
            fieldProxy.FindProperty("m_Text").stringValue = ((int)defaultAutosaveInterval).ToString();
            fieldProxy.ApplyModifiedProperties();

            return true;
        }

        private static void PopulateList<T>(
            ReadOnlyCollection<LockstepGameState> allGameStates,
            SerializedObject proxy,
            string entriesArrayName,
            string postfix,
            Transform list,
            GameObject prefab,
            bool leaveInteractableUnchanged = false,
            bool leaveIsOnUnchanged = false,
            Action<T, LockstepGameState> callback = null)
            where T : LockstepGameStateEntryBase
        {
            T[] entries = new T[allGameStates.Count];
            for (int i = 0; i < allGameStates.Count; i++)
            {
                GameObject inst;
                if (i < list.childCount)
                    inst = list.GetChild(i).gameObject;
                else
                {
                    inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    inst.transform.SetParent(list, false);
                    Undo.RegisterCreatedObjectUndo(inst, "LockstepGameStatesUI entry creation OnBuild");
                }
                SerializedObject instProxy = new SerializedObject(inst);
                instProxy.FindProperty("m_Name").stringValue = allGameStates[i].GameStateInternalName + postfix;
                instProxy.ApplyModifiedProperties();

                T entry = inst.GetComponent<T>();
                entries[i] = entry;

                SerializedObject displayNameTextProxy = new SerializedObject(entry.displayNameText);
                displayNameTextProxy.FindProperty("m_text").stringValue = allGameStates[i].GameStateDisplayName;
                displayNameTextProxy.ApplyModifiedProperties();

                if (entry.toggledImage != null)
                {
                    SerializedObject toggledImageProxy = new SerializedObject(entry.toggledImage);
                    toggledImageProxy.FindProperty("m_Color").colorValue = allGameStates[i].GameStateSupportsImportExport
                        ? entry.goodColor
                        : entry.badColor;
                    toggledImageProxy.ApplyModifiedProperties();

                    SerializedObject mainToggleProxy = new SerializedObject(entry.mainToggle);
                    if (!leaveInteractableUnchanged)
                        mainToggleProxy.FindProperty("m_Interactable").boolValue = allGameStates[i].GameStateSupportsImportExport;
                    if (!leaveIsOnUnchanged)
                        mainToggleProxy.FindProperty("m_IsOn").boolValue = !allGameStates[i].GameStateSupportsImportExport;
                    mainToggleProxy.ApplyModifiedProperties();
                }
                if (callback != null)
                    callback(entry, allGameStates[i]);
            }

            while (list.childCount > allGameStates.Count)
                Undo.DestroyObjectImmediate(list.GetChild(list.childCount - 1).gameObject);

            EditorUtil.SetArrayProperty(proxy.FindProperty(entriesArrayName), entries, (p, v) => p.objectReferenceValue = v);
        }
    }
}
