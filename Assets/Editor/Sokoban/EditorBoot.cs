using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sokoban.EditorTools
{
    /// <summary>
    /// 打开项目时自动加载主场景。
    /// 背景：新 clone 的仓库没有 Library/LastSceneManagerSetup（Library 不入库），
    /// Unity 首次打开会落在一个空场景，必须手动去 Scenes 目录双击 Game.unity。
    /// 本引导只在「当前没有活动场景」时介入，正常会话不打扰。
    /// </summary>
    [InitializeOnLoad]
    static class EditorBoot
    {
        const string MainScenePath = "Assets/Scenes/Game.unity";

        static EditorBoot()
        {
            // delayCall：等编辑器完全初始化、场景加载流程结束后再判断
            EditorApplication.delayCall += OpenMainSceneIfEmpty;
        }

        static void OpenMainSceneIfEmpty()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path)) return;   // 已在某个已保存场景里，不介入

            // 未保存的临时空场景（Untitled）：未修改时静默切换；有未保存内容则先征求用户
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            Debug.Log($"[EditorBoot] 检测到空场景，已自动打开主场景 {MainScenePath}");
        }
    }
}
