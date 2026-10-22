using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 改键（按键重绑）的存档与执行。
///
/// Input System 自带把「绑定覆盖」序列化成 JSON 的能力，
/// 这里只负责三件事：存进 PlayerPrefs、读回来套到 Action 上、以及跑一次「请按新键」。
///
/// 为什么不用 InputActionAsset 的编辑器改键：那是资产，改了就进 git，
/// 而且每个玩家想用的键不一样，必须存在玩家本机。
/// </summary>
public static class InputBindingStore
{
    private const string Key = "settings.input.overrides";

    /// <summary>本机是否存过自定义按键</summary>
    public static bool HasOverrides => !string.IsNullOrEmpty(PlayerPrefs.GetString(Key, string.Empty));

    /// <summary>把存档里的改键覆盖到这份 Action 资产上。没有存档就什么都不做（用默认键）</summary>
    public static void Apply(InputActionAsset asset)
    {
        if (asset == null) return;

        string json = PlayerPrefs.GetString(Key, string.Empty);
        if (string.IsNullOrEmpty(json)) return;

        try
        {
            asset.LoadBindingOverridesFromJson(json);
        }
        catch (System.Exception e)
        {
            // 存档格式可能来自旧版本，读坏了不能把游戏搞崩，退回默认键
            Debug.LogWarning("[改键] 读取本机改键存档失败，先使用默认按键：" + e.Message);
        }
    }

    /// <summary>把当前绑定覆盖存到本机</summary>
    public static void Save(InputActionAsset asset)
    {
        if (asset == null) return;

        try
        {
            PlayerPrefs.SetString(Key, asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[改键] 保存失败：" + e.Message);
        }
    }

    /// <summary>清掉所有改键，回到 assets 里配的默认按键</summary>
    public static void ResetAll(InputActionAsset asset)
    {
        if (asset != null)
        {
            try
            {
                asset.RemoveAllBindingOverrides();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[改键] 恢复默认失败：" + e.Message);
            }
        }

        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }

    /// <summary>把一个 Action 当前生效的按键显示成人话，例如 "Space" / "J"</summary>
    public static string Describe(InputAction action)
    {
        if (action == null) return "—";

        try
        {
            string text = action.GetBindingDisplayString();
            return string.IsNullOrEmpty(text) ? "未绑定" : text;
        }
        catch (System.Exception)
        {
            return "—";
        }
    }

    /// <summary>
    /// 开始一次改键：接下来按下的第一个键会成为这条 Action 的新键。
    /// 改键期间要先把 Action 停用，否则新按的键会同时被当成游戏输入。
    /// onFinished 在成功或取消后都会被调用（调用方用它刷新界面）。
    /// </summary>
    public static InputActionRebindingExtensions.RebindingOperation StartRebind(InputAction action, System.Action onFinished)
    {
        if (action == null) return null;

        action.Disable();

        InputActionRebindingExtensions.RebindingOperation operation = action
            .PerformInteractiveRebinding()                       // 不传索引 = 改第 0 条绑定
            .WithControlsExcluding("<Mouse>/position")           // 鼠标移动/滚轮不该被当成「按键」
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll")
            .WithCancelingThrough("<Keyboard>/escape")           // 按 Esc 取消
            .OnMatchWaitForAnother(0.15f)
            .OnComplete(op => Finish(op, action, onFinished))
            .OnCancel(op => Finish(op, action, onFinished));

        operation.Start();
        return operation;
    }

    private static void Finish(
        InputActionRebindingExtensions.RebindingOperation operation,
        InputAction action,
        System.Action onFinished)
    {
        operation.Dispose();

        // 不管成功还是取消，都把 Action 打开，不然改完键游戏就没输入了
        if (action != null) action.Enable();

        if (onFinished != null) onFinished();
    }
}
