using System;
using System.Threading.Tasks;
using PurrNet.UI;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace PurrNet.Lobby
{
    public class LobbyManager : MonoBehaviour
    {
        [SerializeField] private GameOrchestrator _orchestrator;
        [SerializeField] private ViewStack _stack;

        private LobbyProvider _subscribedProvider;

        private void Start()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            UnsubscribeExternalJoin();
        }

        public void Initialize()
        {
            InitializeAsync().Forget("[LobbyManager] Initialize failed");
        }

        public async Task InitializeAsync()
        {
            var sw = Stopwatch.StartNew();
            GameOrchestrator.active = _orchestrator;
            UnityEngine.Debug.Log($"[LobbyManager] InitializeAsync start (t={sw.ElapsedMilliseconds}ms)");

            if (_orchestrator.sessionProvider)
            {
                UnityEngine.Debug.Log($"[LobbyManager] sessionProvider.Login starting (t={sw.ElapsedMilliseconds}ms)");
                await _orchestrator.sessionProvider.Login(_stack);
                UnityEngine.Debug.Log($"[LobbyManager] sessionProvider.Login done (t={sw.ElapsedMilliseconds}ms)");
            }

            if (_orchestrator.lobbyProvider)
            {
                UnityEngine.Debug.Log($"[LobbyManager] lobbyProvider.Initialize starting (t={sw.ElapsedMilliseconds}ms)");
                await _orchestrator.lobbyProvider.Initialize();
                UnityEngine.Debug.Log($"[LobbyManager] lobbyProvider.Initialize done (t={sw.ElapsedMilliseconds}ms)");
            }

            if (_orchestrator.matchmakingProvider)
            {
                UnityEngine.Debug.Log($"[LobbyManager] matchmakingProvider.Initialize starting (t={sw.ElapsedMilliseconds}ms)");
                await _orchestrator.matchmakingProvider.Initialize();
                UnityEngine.Debug.Log($"[LobbyManager] matchmakingProvider.Initialize done (t={sw.ElapsedMilliseconds}ms)");
            }

            if (_orchestrator.gameAllocator)
            {
                UnityEngine.Debug.Log($"[LobbyManager] gameAllocator.Initialize starting (t={sw.ElapsedMilliseconds}ms)");
                await _orchestrator.gameAllocator.Initialize();
                UnityEngine.Debug.Log($"[LobbyManager] gameAllocator.Initialize done (t={sw.ElapsedMilliseconds}ms)");
            }

            SubscribeExternalJoin();

            UnityEngine.Debug.Log($"[LobbyManager] Pushing MainMenuView (t={sw.ElapsedMilliseconds}ms)");
            _stack.Push<MainMenuView>().Setup(this, _orchestrator);
            UnityEngine.Debug.Log($"[LobbyManager] MainMenuView pushed (t={sw.ElapsedMilliseconds}ms)");
        }

        private void SubscribeExternalJoin()
        {
            UnsubscribeExternalJoin();

            if (_orchestrator.lobbyProvider)
            {
                _subscribedProvider = _orchestrator.lobbyProvider;
                _subscribedProvider.onExternalJoinRequested += OnExternalJoinRequested;
            }
        }

        private void UnsubscribeExternalJoin()
        {
            if (_subscribedProvider)
                _subscribedProvider.onExternalJoinRequested -= OnExternalJoinRequested;
            _subscribedProvider = null;
        }

        private void OnExternalJoinRequested(string lobbyId)
        {
            JoinExternalAsync(lobbyId).Forget("[LobbyManager] External join failed");
        }

        /// <summary>
        /// Handles a platform join request (e.g. an accepted Steam overlay invite):
        /// leaves the current lobby if any, joins the requested one behind a loading
        /// view, and opens the lobby screen.
        /// </summary>
        private async Task JoinExternalAsync(string lobbyId)
        {
            var provider = _orchestrator.lobbyProvider;
            if (!provider || string.IsNullOrEmpty(lobbyId))
                return;

            if (!provider.capabilities.Has(LobbyCapabilities.JoinLobbyById))
                return;

            if (_orchestrator.activeLobby?.id == lobbyId)
                return;

            // Leave the current lobby through the standard path (pops its view too).
            var currentLobbyView = _stack.GetFirstView<LobbyView>();
            if (currentLobbyView)
                currentLobbyView.LeaveLobby();

            var loadingView = _stack.Push<LoadingView>();
            loadingView.Setup("Joining lobby ...");

            try
            {
                var response = await provider.JoinLobby(lobbyId);

                if (!this)
                    return;

                if (!response.success)
                {
                    Toaster.PushError("Failed to join lobby", response.error);
                    return;
                }

                _stack.Push<LobbyView>().Setup(response.lobby, _orchestrator);
            }
            catch (Exception e)
            {
                Toaster.PushError("Failed to join lobby", e);
                Debug.LogException(e);
            }
            finally
            {
                if (loadingView)
                    loadingView.PopMe();
            }
        }
    }
}
