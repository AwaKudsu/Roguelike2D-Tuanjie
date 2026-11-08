using UnityEngine;

/// <summary>
/// 「把投射物打出去」这件事的唯一实现。
///
/// 普通攻击（法师的法杖弹）和技能（火球术）都要发投射物，
/// 要做的事一模一样：算伤害 → 定方向 → 生成 → 交给 Projectile 自己飞。
/// 所以抽到这里，两边都调它 —— 想加「多重射击」「散射」也只改这一处。
///
/// 静态类，不用挂在任何物体上。
/// </summary>
public static class ProjectileLauncher
{
    /// <summary>
    /// 从角色身前一点点生成，免得投射物一出生就和自己脚下的地面重叠。
    ///
    /// 大球要额外抬起来：碰撞体跟着放大，半径 0.7 的球如果还从腰高生成，
    /// 下缘会直接埋进地里 —— 一出生就撞 Ground 判定为命中墙，火球当场消失，
    /// 表现就是「蓄力越久越打不出伤害」。所以出生点的前后与高度都随半径一起抬。
    /// </summary>
    public static Vector2 MuzzlePosition(Transform owner, int facing, float sizeScale = 1f)
    {
        float grow = Mathf.Max(0f, sizeScale - 1f);
        float forward = 0.55f + grow * 0.45f;
        float up = 0.12f + grow * 0.22f;
        return (Vector2)owner.position + new Vector2(forward * facing, up);
    }

    /// <summary>
    /// 发射一组投射物。
    /// </summary>
    /// <param name="data">伤害 / 击退 / 目标层来自这里</param>
    /// <param name="prefab">投射物预制体，必须挂 Projectile 脚本</param>
    /// <param name="count">发几发，1 = 单发直射</param>
    /// <param name="spreadAngle">多发时的扇形张角（度）</param>
    /// <returns>实际成功发射的发数，0 表示参数有问题</returns>
    public static int Fire(
        AttackData data,
        GameObject prefab,
        CharacterStats stats,
        GameObject owner,
        Vector2 origin,
        int facing,
        int count = 1,
        float spreadAngle = 10f,
        float damageMultiplier = 1f,
        float sizeScale = 1f,
        float speedScale = 1f)
    {
        return FireDirected(data, prefab, stats, owner, origin, new Vector2(facing, 0f),
                            count, spreadAngle, damageMultiplier, sizeScale, speedScale);
    }

    /// <summary>
    /// 朝任意方向发射（箭雨用：方向是垂直向下）。
    /// 扇形仍然按「大致朝向」镜像，所以朝左放多重射击不会有一发往回打。
    /// </summary>
    /// <param name="speedOverride">覆盖预制体上的速度（箭雨要把箭压慢一点），0 = 用预制体值</param>
    /// <param name="lifeTimeOverride">覆盖存活时间，0 = 用预制体值</param>
    public static int FireDirected(
        AttackData data,
        GameObject prefab,
        CharacterStats stats,
        GameObject owner,
        Vector2 origin,
        Vector2 direction,
        int count = 1,
        float spreadAngle = 10f,
        float damageMultiplier = 1f,
        float sizeScale = 1f,
        float speedScale = 1f,
        float speedOverride = 0f,
        float lifeTimeOverride = 0f)
    {
        if (data == null)
        {
            Debug.LogWarning("[ProjectileLauncher] AttackData 是空的，什么都没发出去");
            return 0;
        }

        if (prefab == null)
        {
            Debug.LogWarning($"[ProjectileLauncher]「{data.displayName}」没有填投射物预制体", owner);
            return 0;
        }

        Vector2 baseDir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        int facing = baseDir.x < 0f ? -1 : 1;

        count = Mathf.Clamp(count, 1, 8);
        int fired = 0;

        for (int i = 0; i < count; i++)
        {
            // 1 发 = 正前方；3 发 + 10 度 = -10° / 0° / +10°
            // 朝左时整个扇形要镜像，否则朝左放多重射击会往回打
            float offsetAngle = count == 1 ? 0f : (i - (count - 1) * 0.5f) * spreadAngle;
            float angle       = offsetAngle * facing;

            Vector2 shotDir = Quaternion.Euler(0f, 0f, angle) * baseDir;

            GameObject go = Object.Instantiate(prefab, origin, Quaternion.identity);

            if (!go.TryGetComponent(out Projectile projectile))
            {
                Debug.LogWarning($"[ProjectileLauncher] 预制体「{prefab.name}」上没有 Projectile 脚本", go);
                continue;
            }

            // 伤害在发射这一瞬间就算死 —— 飞行途中玩家换装备也不会改变已经打出去的火球
            DamageInfo damage = DamageCalculator.Build(stats, data, owner, origin);

            // 蓄力火球：伤害、大小、速度三样一起加，不然「蓄力」只是视觉上的自我安慰
            if (!Mathf.Approximately(damageMultiplier, 1f))
            {
                damage.Amount *= damageMultiplier;
            }

            projectile.Launch(shotDir, damage, data.targetLayers, sizeScale, speedScale,
                              speedOverride, lifeTimeOverride);
            fired++;
        }

        return fired;
    }
}
