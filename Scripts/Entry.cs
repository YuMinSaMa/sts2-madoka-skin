using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.RestSite;

namespace Test.Scripts;

[ModInitializer("Init")]
public static class Entry
{
    // 替换哪个角色
    private const string TargetCharacterId = "IRONCLAD";

    // spine资源的路径
    private const string SkinPath = "res://spineskins/madoka_skin.tres";
    // Harmony的ID，确保唯一性
    private const string HarmonyId = "sts2.yourid.madokaskin";

    // 缩放倍数（相对于铁甲原始大小，可按实际效果调整）
    private const float ScaleMultiplier = 3.0f;

    private static Resource? _skinData;

    public static void Init()
    {
        _skinData = ResourceLoader.Load<Resource>(SkinPath, null, ResourceLoader.CacheMode.Reuse);

        new Harmony(HarmonyId).PatchAll();
    }

    [HarmonyPatch(typeof(NCreature), nameof(NCreature._Ready))]
    private static class NCreature_Ready_Patch
    {
        private static void Postfix(NCreature __instance)
        {
            if (_skinData == null) return;

            var player = __instance?.Entity?.Player;
            if (player == null) return;

            if (!string.Equals(player.Character.Id.Entry, TargetCharacterId, StringComparison.OrdinalIgnoreCase))
                return;

            var visuals = __instance.Visuals;
            if (visuals == null || !visuals.HasSpineAnimation) return;

            var body = visuals.GetCurrentBody();
            var mega = new MegaSprite(body);

            // 1. 替换骨骼数据
            mega.SetSkeletonDataRes(new MegaSkeletonDataResource(_skinData));

            // 2. 水平翻转（向右看）
            body.Scale = new Vector2(-Math.Abs(body.Scale.X) * ScaleMultiplier, body.Scale.Y * ScaleMultiplier);

            // 3. 立即播放待机动画
            mega.GetAnimationState().SetAnimation("idle_loop", loop: true);
        }
    }

    [HarmonyPatch(typeof(NMerchantCharacter), nameof(NMerchantCharacter._Ready))]
    private static class NMerchantCharacter_Ready_Patch
    {
        private static void Postfix(NMerchantCharacter __instance)
        {
            if (_skinData == null) return;

            var spineSprite = __instance.GetChild(0) as Node2D;
            if (spineSprite == null) return;

            var mega = new MegaSprite(spineSprite);
            mega.SetSkeletonDataRes(new MegaSkeletonDataResource(_skinData));
            mega.GetAnimationState().SetAnimation("relaxed_loop", loop: true);

            // 放大并向右看（X 取负翻转）
            spineSprite.Scale = new Vector2(-Math.Abs(spineSprite.Scale.X) * ScaleMultiplier, spineSprite.Scale.Y * ScaleMultiplier);
        }
    }

    // 选人界面：hook SelectCharacter，当选中铁甲时替换背景场景里的 SpineSprite 为静态立绘
    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
    private static class NCharacterSelectScreen_SelectCharacter_Patch
    {
        private const string CharSelectTexturePath = "res://animations/character_select/ironclad/characterselect_ironclad.png";
        private const string VoicePath = "res://audio/madoka_select.ogg";
        internal static AudioStreamPlayer? _voicePlayer;

        private static void Postfix(NCharacterSelectScreen __instance, NCharacterSelectButton charSelectButton, CharacterModel characterModel)
        {
            // 无论选哪个角色，先停掉语音
            if (GodotObject.IsInstanceValid(_voicePlayer))
            {
                _voicePlayer!.Stop();
                _voicePlayer.QueueFree();
                _voicePlayer = null;
            }

            if (!string.Equals(characterModel.Id.Entry, TargetCharacterId, StringComparison.OrdinalIgnoreCase))
                return;
            var stream = ResourceLoader.Load<AudioStream>(VoicePath);
            GD.Print($"[MadokaSkin] 语音加载结果: {(stream == null ? "失败(null)" : "成功")}");
            if (stream != null)
            {
                _voicePlayer = new AudioStreamPlayer();
                _voicePlayer.Stream = stream;
                _voicePlayer.VolumeDb = 0f;
                _voicePlayer.Bus = "SFX";
                __instance.AddChild(_voicePlayer);
                _voicePlayer.Play();
                GD.Print($"[MadokaSkin] 语音开始播放，Bus={_voicePlayer.Bus}, IsPlaying={_voicePlayer.Playing}");
                _voicePlayer.Connect(AudioStreamPlayer.SignalName.Finished, Callable.From(_voicePlayer.QueueFree));
            }

            // _bgContainer 在场景里叫 "AnimatedBg"，SelectCharacter 刚把 ironclad 背景场景实例化进去
            var bgContainer = __instance.GetNodeOrNull("AnimatedBg");
            if (bgContainer == null) return;

            // 背景场景的根节点就是 IroncladBg，它有一个名叫 "SpineSprite" 的子节点
            var ironcladBg = bgContainer.GetChildCount() > 0 ? bgContainer.GetChild(0) : null;
            if (ironcladBg == null) return;

            var spineNode = ironcladBg.GetNodeOrNull("SpineSprite");
            if (spineNode == null) return;

            // 隐藏 SpineSprite，防止骨骼动画撕裂图片
            spineNode.Set("visible", false);

            // 如果已经添加过 Sprite2D 则不重复添加
            if (ironcladBg.GetNodeOrNull("MadokaLihui") != null) return;

            var texture = ResourceLoader.Load<Texture2D>(CharSelectTexturePath);
            if (texture == null) return;

            // IroncladBg 是 Control 节点，用 TextureRect 铺满整个父节点
            // StretchMode.KeepAspectCovered 保持比例并裁切边缘以铺满
            var rect = new TextureRect();
            rect.Name = "MadokaLihui";
            rect.Texture = texture;
            rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            rect.AnchorLeft   = 0f;
            rect.AnchorTop    = 0f;
            rect.AnchorRight  = 1f;
            rect.AnchorBottom = 1f;
            rect.OffsetLeft   = -450f;
            rect.OffsetTop    = -150f;
            rect.OffsetRight  = -450f;
            rect.OffsetBottom = -150f;

            rect.Scale = new Vector2(1f, 1f);
            ironcladBg.AddChild(rect);
        }
    }

    [HarmonyPatch(typeof(NRestSiteCharacter), nameof(NRestSiteCharacter._Ready))]
    private static class NRestSiteCharacter_Ready_Patch
    {
        private static void Postfix(NRestSiteCharacter __instance)
        {
            if (_skinData == null) return;
            if (__instance.Player?.Character?.Id.Entry == null) return;
            if (!string.Equals(__instance.Player.Character.Id.Entry, TargetCharacterId, StringComparison.OrdinalIgnoreCase))
                return;

            var spineNode = __instance.GetNodeOrNull<Node2D>("SpineSprite");
            if (spineNode == null) return;

            var mega = new MegaSprite(spineNode);
            mega.SetSkeletonDataRes(new MegaSkeletonDataResource(_skinData));
            mega.GetAnimationState().SetAnimation("flatline", loop: true);

            spineNode.Scale = new Vector2(-Math.Abs(spineNode.Scale.X) * 2.0f, spineNode.Scale.Y * 2.0f);
            spineNode.Position = new Vector2(spineNode.Position.X, spineNode.Position.Y + 100f);
            spineNode.Rotation = -20f * (float)Math.PI / 180f;
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun))]
    private static class NCharacterSelectScreen_BeginRun_Patch
    {
        private static void Prefix()
        {
            if (GodotObject.IsInstanceValid(NCharacterSelectScreen_SelectCharacter_Patch._voicePlayer))
            {
                NCharacterSelectScreen_SelectCharacter_Patch._voicePlayer!.Stop();
                NCharacterSelectScreen_SelectCharacter_Patch._voicePlayer.QueueFree();
                NCharacterSelectScreen_SelectCharacter_Patch._voicePlayer = null;
            }
        }
    }
}
