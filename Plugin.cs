using System;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Hazel;
using InnerNet;
using UnityEngine;

namespace DashGhostMod
{
    [BepInPlugin("com.dash.ghostmod", "DashGhostMod", "1.0.0")]
    public class Plugin : BasePlugin
    {
        public const string DonoFriendCode = "squishyrag#2321";

        public override void Load()
        {
            var harmony = new Harmony("com.dash.ghostmod");
            harmony.PatchAll();
            Log.LogInfo("DashGhostMod v1.0.0 carregado!");
        }

        public static bool EhSalaDoDash()
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost)
                return false;

            if (PlayerControl.LocalPlayer == null)
                return false;

            string fc = PlayerControl.LocalPlayer.FriendCode ?? "";
            return fc.Equals(DonoFriendCode, StringComparison.OrdinalIgnoreCase);
        }

        public static string GerarListaFuncoes()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<b>[Funções da Partida]</b>");

            foreach (var player in PlayerControl.AllPlayerControls)
            {
                if (player == null || player.Data == null) continue;

                string nome = player.Data.PlayerName;
                string funcao = ObterNomeDaFuncao(player);
                sb.AppendLine($"{nome} - {funcao}");
            }

            return sb.ToString().TrimEnd();
        }

        private static string ObterNomeDaFuncao(PlayerControl player)
        {
            if (player.Data.Role == null)
                return "Tripulante";

            string niceName = player.Data.Role.NiceName;
            if (!string.IsNullOrEmpty(niceName) && niceName != "STRMISS")
                return niceName;

            return player.Data.Role.IsImpostor ? "Impostor" : player.Data.Role.Role.ToString();
        }

        public static void EnviarChatPrivado(PlayerControl destinatario, string mensagem)
        {
            if (AmongUsClient.Instance == null || destinatario == null) return;

            int clientId = AmongUsClient.Instance.GetClientIdFromCharacter(destinatario);
            if (clientId < 0) return;

            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(
                destinatario.NetId,
                (byte)RpcCalls.SendChat,
                SendOption.Reliable,
                clientId
            );
            writer.Write(mensagem);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        public static void PararMovimentoFantasma()
        {
            var local = PlayerControl.LocalPlayer;
            if (local == null) return;

            if (local.MyPhysics != null && local.MyPhysics.body != null)
            {
                local.MyPhysics.body.velocity = Vector2.zero;
            }
        }
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
    public static class LocalSendChatPatch
    {
        public static bool Prefix(ChatController __instance)
        {
            string texto = __instance.freeChatField.Text.Trim();
            if (!texto.Equals("/role", StringComparison.OrdinalIgnoreCase))
                return true;

            __instance.freeChatField.Clear();
            __instance.quickChatMenu.Clear();

            if (PlayerControl.LocalPlayer != null &&
                PlayerControl.LocalPlayer.Data != null &&
                PlayerControl.LocalPlayer.Data.IsDead)
            {
                string lista = Plugin.GerarListaFuncoes();
                __instance.AddChat(PlayerControl.LocalPlayer, lista);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    public static class RemoteAddChatPatch
    {
        public static bool Prefix(PlayerControl sourcePlayer, string chatText)
        {
            if (sourcePlayer == null || sourcePlayer.Data == null || string.IsNullOrWhiteSpace(chatText))
                return true;

            if (PlayerControl.LocalPlayer != null && sourcePlayer.PlayerId == PlayerControl.LocalPlayer.PlayerId)
                return true;

            if (!chatText.Trim().Equals("/role", StringComparison.OrdinalIgnoreCase))
                return true;

            if (Plugin.EhSalaDoDash() && sourcePlayer.Data.IsDead)
            {
                string lista = Plugin.GerarListaFuncoes();
                Plugin.EnviarChatPrivado(sourcePlayer, lista);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    public static class GhostPinchZoomPatch
    {
        private const float ZoomPadrao = 3.0f;
        private const float ZoomMaximo = 30.0f;
        private static float zoomAtual = ZoomPadrao;
        private static bool estavaEmPinca = false;

        public static void Postfix(HudManager __instance)
        {
            if (PlayerControl.LocalPlayer == null || PlayerControl.LocalPlayer.Data == null)
                return;

            bool estaMorto = PlayerControl.LocalPlayer.Data.IsDead;
            bool emReuniao = MeetingHud.Instance != null;
            bool emTask = Minigame.Instance != null;
            bool chatAberto = __instance.Chat != null && __instance.Chat.IsOpenOrOpening;

            if (!estaMorto || emReuniao || emTask || chatAberto)
            {
                estavaEmPinca = false;
                if (Math.Abs(zoomAtual - ZoomPadrao) > 0.01f)
                {
                    zoomAtual = ZoomPadrao;
                    AplicarZoom(ZoomPadrao);
                }
                return;
            }

            if (Input.touchCount >= 2)
            {
                Touch dedo0 = Input.GetTouch(0);
                Touch dedo1 = Input.GetTouch(1);

                if (dedo0.phase == TouchPhase.Ended || dedo0.phase == TouchPhase.Canceled ||
                    dedo1.phase == TouchPhase.Ended || dedo1.phase == TouchPhase.Canceled)
                {
                    estavaEmPinca = false;
                    Plugin.PararMovimentoFantasma();
                    return;
                }

                Vector2 posAnterior0 = dedo0.position - dedo0.deltaPosition;
                Vector2 posAnterior1 = dedo1.position - dedo1.deltaPosition;

                float distanciaAnterior = (posAnterior0 - posAnterior1).magnitude;
                float distanciaAtual = (dedo0.position - dedo1.position).magnitude;

                float diferenca = distanciaAnterior - distanciaAtual;

                estavaEmPinca = true;
                zoomAtual = Mathf.Clamp(zoomAtual + diferenca * 0.03f, ZoomPadrao, ZoomMaximo);
                AplicarZoom(zoomAtual);
                Plugin.PararMovimentoFantasma();
            }
            else
            {
                if (estavaEmPinca)
                {
                    estavaEmPinca = false;
                    Plugin.PararMovimentoFantasma();
                }

                if (Math.Abs(Input.mouseScrollDelta.y) > 0.01f)
                {
                    zoomAtual = Mathf.Clamp(zoomAtual - Input.mouseScrollDelta.y * 1.5f, ZoomPadrao, ZoomMaximo);
                    AplicarZoom(zoomAtual);
                }
            }
        }

        private static void AplicarZoom(float tamanhoCamera)
        {
            if (Camera.main != null)
                Camera.main.orthographicSize = tamanhoCamera;

            if (HudManager.Instance != null && HudManager.Instance.UICamera != null)
                HudManager.Instance.UICamera.orthographicSize = ZoomPadrao;
        }
    }
}
