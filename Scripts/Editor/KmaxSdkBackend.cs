using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample.Editor {
    /// <summary>
    /// Selects which of the two vendored Kmax SDKs compiles into the project.
    /// Both SDKs declare the <c>KmaxXR</c> namespace and share five type names, so their assembly
    /// definitions are constrained on <see cref="AioDefineSymbol"/>: when the symbol is absent the
    /// Kmax XR Core SDK compiles, when it is present the Kmax AIO K1 SDK compiles instead.
    /// </summary>
    public static class KmaxSdkBackend {
        public const string AioDefineSymbol = "KMAX_AIO_K1";

        private const string MenuXrCore = "Kmax/SDK Backend/XR Core 2.5.2";
        private const string MenuAioK1 = "Kmax/SDK Backend/AIO K1 1.2.0";
        private const string LogPrefix = "[Kmax] ";

        private static readonly NamedBuildTarget[] SupportedBuildTargets = new NamedBuildTarget[] {
            NamedBuildTarget.Standalone,
            NamedBuildTarget.Android,
            NamedBuildTarget.WebGL,
            NamedBuildTarget.WindowsStoreApps
        };

        /// <summary>
        /// True when the AIO K1 SDK is the backend the active build target compiles against.
        /// </summary>
        public static bool IsAioBackendActive {
            get {
                return HasSymbol(GetActiveBuildTarget(), AioDefineSymbol);
            }
        }

        [MenuItem(MenuXrCore, false, 100)]
        private static void SelectXrCoreBackend() {
            ApplyBackend(false);
        }

        [MenuItem(MenuXrCore, true)]
        private static bool ValidateXrCoreBackend() {
            Menu.SetChecked(MenuXrCore, !IsAioBackendActive);
            return true;
        }

        [MenuItem(MenuAioK1, false, 101)]
        private static void SelectAioK1Backend() {
            ApplyBackend(true);
        }

        [MenuItem(MenuAioK1, true)]
        private static bool ValidateAioK1Backend() {
            Menu.SetChecked(MenuAioK1, IsAioBackendActive);
            return true;
        }

        private static void ApplyBackend(bool useAioK1) {
            List<NamedBuildTarget> buildTargets = CollectBuildTargets();
            int changedCount = 0;

            for (int i = 0; i < buildTargets.Count; i++) {
                if (SetSymbol(buildTargets[i], AioDefineSymbol, useAioK1)) {
                    changedCount++;
                }
            }

            if (changedCount == 0) {
                Debug.Log($"{LogPrefix}SDK backend is already {DescribeBackend(useAioK1)}.");
                return;
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"{LogPrefix}SDK backend switched to {DescribeBackend(useAioK1)} " +
                $"across {changedCount} build target(s). Unity will now recompile.");

            if (useAioK1 && !IsWindowsBuildTarget(EditorUserBuildSettings.activeBuildTarget)) {
                Debug.LogWarning($"{LogPrefix}The AIO K1 SDK only builds for Windows Standalone and " +
                    $"Windows Store. The active build target is {EditorUserBuildSettings.activeBuildTarget}, " +
                    "so no Kmax runtime code will be compiled into it.");
            }
        }

        private static List<NamedBuildTarget> CollectBuildTargets() {
            List<NamedBuildTarget> buildTargets = new List<NamedBuildTarget>(SupportedBuildTargets.Length + 1);
            buildTargets.AddRange(SupportedBuildTargets);

            NamedBuildTarget activeBuildTarget = GetActiveBuildTarget();
            if (!activeBuildTarget.Equals(NamedBuildTarget.Unknown) && !buildTargets.Contains(activeBuildTarget)) {
                buildTargets.Add(activeBuildTarget);
            }

            return buildTargets;
        }

        private static NamedBuildTarget GetActiveBuildTarget() {
            BuildTargetGroup buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            if (buildTargetGroup == BuildTargetGroup.Unknown) {
                Debug.LogWarning($"{LogPrefix}Could not resolve a build target group for " +
                    $"{EditorUserBuildSettings.activeBuildTarget}; its scripting define symbols were left untouched.");
                return NamedBuildTarget.Unknown;
            }

            return NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
        }

        private static bool HasSymbol(NamedBuildTarget buildTarget, string symbol) {
            if (buildTarget.Equals(NamedBuildTarget.Unknown)) {
                return false;
            }

            string[] symbols;
            PlayerSettings.GetScriptingDefineSymbols(buildTarget, out symbols);

            for (int i = 0; i < symbols.Length; i++) {
                if (string.Equals(symbols[i], symbol, StringComparison.Ordinal)) {
                    return true;
                }
            }

            return false;
        }

        private static bool SetSymbol(NamedBuildTarget buildTarget, string symbol, bool enabled) {
            if (buildTarget.Equals(NamedBuildTarget.Unknown)) {
                return false;
            }

            string[] currentSymbols;
            PlayerSettings.GetScriptingDefineSymbols(buildTarget, out currentSymbols);

            List<string> updatedSymbols = new List<string>(currentSymbols.Length + 1);
            bool alreadyDefined = false;

            for (int i = 0; i < currentSymbols.Length; i++) {
                if (string.Equals(currentSymbols[i], symbol, StringComparison.Ordinal)) {
                    alreadyDefined = true;
                    continue;
                }

                updatedSymbols.Add(currentSymbols[i]);
            }

            if (alreadyDefined == enabled) {
                return false;
            }

            if (enabled) {
                updatedSymbols.Add(symbol);
            }

            PlayerSettings.SetScriptingDefineSymbols(buildTarget, updatedSymbols.ToArray());
            return true;
        }

        private static bool IsWindowsBuildTarget(BuildTarget buildTarget) {
            return buildTarget == BuildTarget.StandaloneWindows ||
                buildTarget == BuildTarget.StandaloneWindows64 ||
                buildTarget == BuildTarget.WSAPlayer;
        }

        private static string DescribeBackend(bool useAioK1) {
            if (useAioK1) {
                return "Kmax AIO K1 SDK 1.2.0";
            }

            return "Kmax XR Core SDK 2.5.2";
        }
    }
}