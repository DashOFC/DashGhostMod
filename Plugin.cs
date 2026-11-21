using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace DashGhostMod
{
    [BepInPlugin("com.dash.ghostmod", "DashGhostMod", "1.1.0")]
    public class Plugin : BasePlugin
    {
        public static bool ZoomFantasmaAtivo = true;
        public static float NivelZoomAtual = 1f;

        public const float ZoomMinimo = 1f;
        public const float ZoomMaximo = 10f;
        public const float VelocidadeScrollPC = 1.2f;
        public const float SensibilidadePincaMobile = 0.015f;

        private static float tamanhoCameraOriginal = -1f;

        public override void Load()
        {
            var harmony = new Harmony("com.dash.ghostmod");
            harmony.PatchAll();
            Log.LogInfo("DashGhostMod v1.1.0 (PC + Mobile) carregado!");
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

            bool alterouZoom = false;

            // 1. Controle no PC: Scroll do Mouse
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f))
            {
                try { scroll = Input.GetAxis("Mouse ScrollWheel") * 10f; } catch { }
            }

            if (!Mathf.Approximately(scroll, 0f))
            {
                // Scroll para baixo (negativo) afasta a câmera (aumenta o alcance de visão)
                // Scroll para cima (positivo) aproxima a câmera
                NivelZoomAtual -= scroll * VelocidadeScrollPC;
                alterouZoom = true;
            }

            // 2. Controle no Celular: Pinça com 2 dedos
            if (Input.touchCount == 2)
            {
                Touch toque0 = Input.GetTouch(0);
                Touch toque1 = Input.GetTouch(1);

                Vector2 posAnterior0 = toque0.position - toque0.deltaPosition;
                Vector2 posAnterior1 = toque1.position - toque1.deltaPosition;

                float distAnterior = (posAnterior0 - posAnterior1).magnitude;
                float distAtual = (toque0.position - toque1.position).magnitude;
                float diferenca = distAtual - distAnterior;

                if (!Mathf.Approximately(diferenca, 0f))
                {
                    // Fechar os dedos afasta a câmera; abrir aproxima
                    NivelZoomAtual -= diferenca * SensibilidadePincaMobile;
                    alterouZoom = true;
                }
            }

            if (alterouZoom)
            {
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

            float novoTamanho = tamanhoCameraOriginal * NivelZoomAtual;
            Camera.main.orthographicSize = novoTamanho;

            if (HudManager.Instance != null && HudManager.Instance.UICamera != null)
            {
                // Mantém a UI no tamanho normal para os botões e o chat não encolherem
                HudManager.Instance.ShadowQuad?.gameObject.SetActive(NivelZoomAtual <= 1.05f);
            }
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

    // Evita que o fantasma fique deslizando/andando sozinho enquanto faz pinça no celular
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
    public static class GhostMovementStabilizerPatch
    {
        public static bool Prefix(PlayerPhysics __instance)
        {
            if (__instance == null || __instance.myPlayer == null || !__instance.myPlayer.AmOwner)
                return true;

            if (Plugin.EstaMorto() && Input.touchCount >= 2 && __instance.body != null)
            {
                __instance.body.velocity = Vector2.zero;
                return false;
            }

            return true;
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
                    "<b>[DashGhostMod]</b> Zoom de fantasma ativado! Use o <b>scroll do mouse</b> (PC) ou <b>pinça na tela</b> (Celular)."
                );
            }

            return false;
        }
    }
}
