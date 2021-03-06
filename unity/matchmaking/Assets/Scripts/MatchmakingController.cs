﻿using System;
using System.Collections;
using System.Collections.Generic;
using Gs2.Core;
using Gs2.Core.Model;
using Gs2.Gs2Matchmaking.Model;
using Gs2.Sample.Core;
using Gs2.Unity.Gs2Matchmaking.Model;
using Gs2.Unity.Gs2Matchmaking.Result;
using LitJson;
using UnityEngine;
using UnityEngine.Events;

namespace Gs2.Sample.Matchmaking
{
    public class MatchmakingController : MonoBehaviour
    {
        /// <summary>
        /// GS2-Matchmaking の設定値
        /// </summary>
        [SerializeField]
        public Gs2MatchmakingSetting gs2MatchmakingSetting;

        /// <summary>
        /// Gs2Client
        /// </summary>
        [SerializeField]
        public Gs2Client gs2Client;

        /// <summary>
        /// 参加しているギャザリング
        /// </summary>
        private EzGathering _gathering;

        /// <summary>
        /// ギャザリングに参加しているプレイヤーIDリスト
        /// </summary>
        private readonly List<string> _joinedPlayerIds = new List<string>();

        /// <summary>
        /// マッチメイキングが完了したか
        /// </summary>
        private bool _complete;

        private void Validate()
        {
            if (!gs2MatchmakingSetting)
            {
                gs2MatchmakingSetting = Gs2Util.LoadGlobalGameObject<Gs2MatchmakingSetting>("Gs2Settings");
            }

            if (!gs2Client)
            {
                gs2Client = Gs2Util.LoadGlobalGameObject<Gs2Client>("Gs2Settings");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        public void PushNotificationHandler(NotificationMessage message)
        {
            Debug.Log(message.issuer);
            
            if (!message.issuer.StartsWith("Gs2Matchmaking:")) return;
            
            if (message.issuer.EndsWith(":Join"))
            {
                var notification = JsonMapper.ToObject<JoinNotification>(message.payload);
                _joinedPlayerIds.Add(notification.joinUserId);
                gs2MatchmakingSetting.onJoinPlayer.Invoke(_gathering, notification.joinUserId);
                gs2MatchmakingSetting.onUpdateJoinedPlayerIds.Invoke(_gathering, _joinedPlayerIds);
            }
            else if (message.issuer.EndsWith(":Leave"))
            {
                var notification = JsonMapper.ToObject<LeaveNotification>(message.payload);
                _joinedPlayerIds.Remove(notification.leaveUserId);
                gs2MatchmakingSetting.onLeavePlayer.Invoke(_gathering, notification.leaveUserId);
                gs2MatchmakingSetting.onUpdateJoinedPlayerIds.Invoke(_gathering, _joinedPlayerIds);
            }
            else if (message.issuer.EndsWith(":Complete"))
            {
                if (_gathering != null)
                {
                    // Joinと同時にマッチメイキングが成立する場合
                    // DoMatchmaking の応答より先にマッチメイキング完了通知が届くことがある
                    gs2MatchmakingSetting.onMatchmakingComplete.Invoke(_gathering, _joinedPlayerIds);
                }

                _complete = true;
            }
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        /// <returns></returns>
        public void Initialize()
        {
            Validate();

            _complete = false;
            
            gs2Client.profile.Gs2Session.OnNotificationMessage += PushNotificationHandler;
        }

        /// <summary>
        /// 誰でもいいので参加者を募集するギャザリングを新規作成
        /// </summary>
        /// <param name="callback"></param>
        /// <param name="capacity"></param>
        /// <returns></returns>
        public IEnumerator SimpleMatchmakingCreateGathering(
            UnityAction<AsyncResult<EzCreateGatheringResult>> callback,
            int capacity
        )
        {
            Validate();

            var request = Gs2Util.LoadGlobalGameObject<MatchmakingRequest>("MatchmakingRequest");

            AsyncResult<EzCreateGatheringResult> result = null;
            yield return gs2Client.client.Matchmaking.CreateGathering(
                r => { result = r; },
                request.gameSession,
                gs2MatchmakingSetting.matchmakingNamespaceName,
                new EzPlayer
                {
                    RoleName = "default"
                },
                new List<EzCapacityOfRole>
                {
                    new EzCapacityOfRole
                    {
                        RoleName = "default",
                        Capacity = capacity
                    },
                },
                new List<string>(),
                new List<EzAttributeRange>()
            );
            
            if (result.Error != null)
            {
                gs2MatchmakingSetting.onError.Invoke(
                    result.Error
                );
                callback.Invoke(result);
                yield break;
            }

            _joinedPlayerIds.Clear();
            _gathering = result.Result.Item;
            _joinedPlayerIds.Add(request.gameSession.AccessToken.userId);

            gs2MatchmakingSetting.onUpdateJoinedPlayerIds.Invoke(_gathering, _joinedPlayerIds);

            callback.Invoke(result);
        }

        /// <summary>
        /// 既存のギャザリングに参加する
        /// </summary>
        /// <param name="callback"></param>
        /// <returns></returns>
        public IEnumerator SimpleMatchmakingJoinGathering(
            UnityAction<AsyncResult<EzDoMatchmakingResult>> callback
        )
        {
            Validate();

            var request = Gs2Util.LoadGlobalGameObject<MatchmakingRequest>("MatchmakingRequest");

            AsyncResult<EzDoMatchmakingResult> result = null;
            string contextToken = null;
            while (true)
            {
                yield return gs2Client.client.Matchmaking.DoMatchmaking(
                    r => { result = r; },
                    request.gameSession,
                    gs2MatchmakingSetting.matchmakingNamespaceName,
                    new EzPlayer
                    {
                        RoleName = "default"
                    },
                    contextToken
                );
            
                if (result.Error != null)
                {
                    gs2MatchmakingSetting.onError.Invoke(
                        result.Error
                    );

                    callback.Invoke(result);
                    yield break;
                }

                if (result.Result.Item != null)
                {
                    _gathering = result.Result.Item;
                    callback.Invoke(result);

                    if (_complete)
                    {
                        gs2MatchmakingSetting.onMatchmakingComplete.Invoke(_gathering, _joinedPlayerIds);
                    }
                    
                    yield break;
                }

                contextToken = result.Result.MatchmakingContextToken;
            }
        }

        /// <summary>
        /// マッチメイキングをキャンセルしたとき
        /// </summary>
        /// <param name="callback"></param>
        /// <returns></returns>
        public IEnumerator CancelMatchmaking(
            UnityAction<AsyncResult<EzCancelMatchmakingResult>> callback
        )
        {
            Validate();

            var request = Gs2Util.LoadGlobalGameObject<MatchmakingRequest>("MatchmakingRequest");

            AsyncResult<EzCancelMatchmakingResult> result = null;
            yield return gs2Client.client.Matchmaking.CancelMatchmaking(
                r => { result = r; },
                request.gameSession,
                gs2MatchmakingSetting.matchmakingNamespaceName,
                _gathering.Name
            );
        
            if (result.Error != null)
            {
                gs2MatchmakingSetting.onError.Invoke(
                    result.Error
                );
                callback.Invoke(result);
                yield break;
            }

            gs2MatchmakingSetting.onMatchmakingCancel.Invoke(
                _gathering
            );

            _gathering = null;
            
            callback.Invoke(result);
        }
    }
}