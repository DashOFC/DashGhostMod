using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace DashGhostMod
{
    [BepInPlugin("com.dash.ghostmod", "DashGhostMod", "1.2.0")]
    public class Plugin : BasePlugin
    {
        public static bool ZoomFantasmaAtivo = true;
        public static float NivelZoomAtual = 1f;

        public const float ZoomMinimo = 1f;
        public const float ZoomMaximo = 10f;
        public const float VelocidadeScrollPC = 1.2f;

        private static float tamanhoCameraOriginal = -1f;

        public override void Load()
        {
            var harmony = new Harmony("com.dash.ghostmod");
            harmony.PatchAll();
            Log.LogInfo("DashGhostMod v1.2.0 (PC Scroll) carregado!");
        }

        public static bool EstaMorto()
        {
            return PlayerControl.LocalPlayer != null
                && PlayerControl.LocalPlayer.Data != null
                && PlayerControl.LocalPlayer.Data.IsDead;
        }

        public static bool PodeUsarZoomAgora()
        {
            if (!ZoomFantasmaAtivo || !EstaMorto())
                return false;

            if (MeetingHud.Instance != null)
                return false;

            if (Minigame.Instance != null)
                return false;

            if (HudManager.Instance != null && HudManager.Instance.Chat != null && HudManager.Instance.Chat.IsOpenOrOpening)
                return false;

            return true;
        }

        public static void ResetarZoom()
        {
            NivelZoomAtual = ZoomMinimo;
            AplicarZoomNaCamera();
        }

        public static void AtualizarZoom()
        {
            if (Camera.main == null)
                return;

            if (tamanhoCameraOriginal <= 0f)
                tamanhoCameraOriginal = Camera.main.orthographicSize;

            if (!PodeUsarZoomAgora())
            {
                if (Mathf.Abs(NivelZoomAtual - ZoomMinimo) > 0.01f)
                    ResetarZoom();
                return;
            }

            // Controle exclusivo pelo Scroll do Mouse
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f))
            {
                try { scroll = Input.GetAxis("Mouse ScrollWheel") * 10f; } catch { }
            }

            if (!Mathf.Approximately(scroll, 0f))
            {
                // Scroll para baixo (negativo) afasta a câmera; para cima (positivo) aproxima
                NivelZoomAtual -= scroll * VelocidadeScrollPC;
                NivelZoomAtual = Mathf.Clamp(NivelZoomAtual, ZoomMinimo, ZoomMaximo);
                AplicarZoomNaCamera();
            }
        }

        public static void AplicarZoomNaCamera()
        {
            if (Camera.main == null)
                return;

            if (tamanhoCameraOriginal <= 0f)
                tamanhoCameraOriginal = Camera.main.orthographicSize;

            Camera.main.orthographicSize = tamanhoCameraOriginal * NivelZoomAtual;
        }

        public static void MostrarMensagemLocal(ChatController chat, string mensagem)
        {
            if (chat == null || PlayerControl.LocalPlayer == null)
                return;

            chat.AddChat(PlayerControl.LocalPlayer, mensagem);
        }
    }

    // Atualiza o zoom a cada frame durante a partida
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    public static class HudManagerZoomPatch
    {
        public static void Postfix()
        {
            Plugin.AtualizarZoom();
        }
    }

    // Garante reset total da câmera ao abrir ou fechar a votação
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Close))]
    public static class MeetingHudCloseZoomPatch
    {
        public static void Postfix()
        {
            Plugin.ResetarZoom();
        }
    }

    // Comando /role no chat (visível apenas para você)
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
    public static class GhostRoleCommandPatch
    {
        public static bool Prefix(ChatController __instance)
        {
            if (__instance == null || __instance.freeChatField == null)
                return true;

            string texto = __instance.freeChatField.Text.Trim();
            if (!texto.Equals("/role", StringComparison.OrdinalIgnoreCase))
                return true;

            __instance.freeChatField.Clear();
            if (__instance.quickChatMenu != null)
                __instance.quickChatMenu.Clear();

            if (!Plugin.EstaMorto())
            {
                Plugin.MostrarMensagemLocal(
                    __instance,
                    "<b>[DashGhostMod]</b> O comando <b>/role</b> só funciona depois que você morrer."
                );
                return false;
            }

            Plugin.ZoomFantasmaAtivo = !Plugin.ZoomFantasmaAtivo;
            if (!Plugin.ZoomFantasmaAtivo)
            {
                Plugin.ResetarZoom();
                Plugin.MostrarMensagemLocal(
                    __instance,
                    "<b>[DashGhostMod]</b> Zoom de fantasma desativado (1x)."
                );
            }
            else
            {
                Plugin.MostrarMensagemLocal(
                    __instance,
                    "<b>[DashGhostMod]</b> Zoom de fantasma ativado! Use o <b>scroll do mouse</b> para aproximar ou afastar."
                );
            }

            return false;
        }
    }
}
