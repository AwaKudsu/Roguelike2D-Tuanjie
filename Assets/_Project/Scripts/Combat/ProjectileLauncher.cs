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
    /// <summary>从角色身前一点点生成，免得投射物一出生就和自己脚下的地面重叠</summary>
    public static Vector2 MuzzlePosition(Transform owner, int facing, float forwardOffset = 0.55f)
    {
        Vector2 p = owner.position;
        return p + new Vector2(forwardOffset * facing, 0f);
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

        count = Mathf.Clamp(count, 1, 8);
        int fired = 0;

        for (int i = 0; i < count; i++)
        {
            // 1 发 = 正前方；3 发 + 10 度 = -10° / 0° / +10°
            // 朝左时整个扇形要镜像，否则朝左放多重射击会往回打
            float offsetAngle = count == 1 ? 0f : (i - (count - 1) * 0.5f) * spreadAngle;
            float angle       = offsetAngle * facing;

            Vector2 direction = Quaternion.Euler(0f, 0f, angle) * new Vector2(facing, 0f);

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

            projectile.Launch(direction, damage, data.targetLayers, sizeScale, speedScale);
            fired++;
        }

        return fired;
    }
}
