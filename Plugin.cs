using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System;
using BoltInternal;
using UnityEngine.Playables;
using BestHTTP.ServerSentEvents;
using Bolt;
using UnityEngine.SceneManagement;

namespace MyFirstPlugin
{
    [BepInPlugin("360rotatehack.com", "rotate_hack", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private static float Hor = 10f; 
        private static float Ver = 10f; 

        internal static new ManualLogSource Logger;
        private static Harmony harmony;
        private bool isRotating = false;
        private bool isAimbotActive = false;
        private List<FirstPersonMover> players = new List<FirstPersonMover>();

        private float predictionTime = 0.3f;
        private Dictionary<FirstPersonMover, Vector3> previousPositions = new Dictionary<FirstPersonMover, Vector3>();
        private Dictionary<FirstPersonMover, Vector3> calculatedVelocities = new Dictionary<FirstPersonMover, Vector3>();

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            harmony = new Harmony("360rotatehack.com");
            harmony.PatchAll();

        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                isAimbotActive = !isAimbotActive;
                Logger.LogInfo($"Aimbot is now {(isAimbotActive ? "enabled" : "disabled")}");
            }

            //if (Input.GetKeyDown(KeyCode.F7))
            //{
                UseAi();
            //}

                if (isAimbotActive)
            {
                AimAtNearestPlayer();
            }

            UpdatePlayerList();
            CalculatePlayerVelocities();
        }

        private void CalculatePlayerVelocities()
        {
            foreach (var player in players)
            {
                if (!previousPositions.ContainsKey(player))
                {
                    previousPositions[player] = player.transform.position;
                    calculatedVelocities[player] = Vector3.zero;
                    continue;
                }

                Vector3 currentPosition = player.transform.position;
                Vector3 velocity = (currentPosition - previousPositions[player]) / Time.deltaTime;
                calculatedVelocities[player] = velocity;

                previousPositions[player] = currentPosition;
            }
        }

        // Поле для включения аима
        //public bool isAimbotEnabled = false;
        public float aimbotRange = 13.0f; // Максимальная дистанция для активации аима

        private void AimAtNearestPlayer()
        {
            FirstPersonMover mainPlayer = null;
            FirstPersonMover nearestPlayer = null;
            float minDistance = float.MaxValue;

            // Find the main player
            foreach (var character in players)
            {
                if (character != null && character.IsMainPlayer())
                {
                    mainPlayer = character;
                    break;
                }
            }

            if (mainPlayer == null)
            {
                return;
            }

            //if (!Input.GetMouseButton(0)) { return; }

            // Find the nearest player
            foreach (var character in players)
            {
                if (character != null && !character.IsMainPlayer())
                {
                    float distance = Vector3.Distance(mainPlayer.transform.position, character.transform.position);
                    if (distance < minDistance && character.IsAttachedAndAlive())
                    {
                        minDistance = distance;
                        nearestPlayer = character;
                    }
                }
            }

            if (nearestPlayer == null)
            {
                return;
            }

            // Find the nearest body part of the nearest player
            /*            Vector3 closestBodyPartPosition = Vector3.zero;
                        float closestPartDistance = float.MaxValue;

                        foreach (var bodyPart in nearestPlayer.GetAllBaseBodyParts())
                        {
                            float partDistance = Vector3.Distance(mainPlayer.transform.position, bodyPart.transform.position);
                            if (partDistance < closestPartDistance)
                            {
                                closestPartDistance = partDistance;
                                closestBodyPartPosition = bodyPart.transform.position;
                            }
                        }*/

            bool usingbow = mainPlayer.GetEquippedWeaponType() == WeaponType.Bow;

            Vector3 aimOffset = Vector3.zero;
            //aimOffset.y = 0.2f;
            // Вычисляем направление до противника
            Vector3 vector = nearestPlayer.GetPositionForAIToAimAt(mainPlayer.GetEquippedWeaponType() == WeaponType.Bow) + aimOffset - mainPlayer.transform.position;


            // Скорость противника
            //Vector3 _estimatedPlayerVelocity = nearestPlayer.GetVelocity();
            //float d = vector.magnitude / BoltGlobalEventListenerSingleton<ProjectileManager>.Instance.GetArrowVelocity();
            //vector = vector + calculatedVelocities[nearestPlayer] * d;
            if (vector.magnitude > 13 && !usingbow) return;
            // Предугадывание будущей позиции
            vector = vector + calculatedVelocities[nearestPlayer] * predictionTime;


            // Получаем угол на который нужно повернуться
            float rotationYFromDirection = WorldQueries.GetRotationYFromDirection(vector);
            float num = Mathf.DeltaAngle(mainPlayer.IsRidingOtherCharacter() ? (mainPlayer.transform.eulerAngles.y + mainPlayer.GetTorsoRotationY()) : mainPlayer.transform.eulerAngles.y, rotationYFromDirection);
            
            // Поворачиваем персонажа по горизонтали
            UpdateRotationTowardsTarget(num, mainPlayer);

            try
            {
                if (!(mainPlayer.GetEquippedWeaponType() == WeaponType.Bow))
                {
                    // Вертикальная наводка
                    float rotationXFromDirection = WorldQueries.GetRotationXFromDirection(nearestPlayer.GetPositionForAIToAimAt(false) + aimOffset - mainPlayer.transform.position);
                    float relativeRotationX = Mathf.DeltaAngle(mainPlayer.GetTotalTilt(), rotationXFromDirection);
                    UpdateVerticalRotationTowardsTarget(relativeRotationX, mainPlayer);
                }
                else
                {
                    float rotationXFromDirection = WorldQueries.GetRotationXFromDirection(nearestPlayer.GetPositionForAIToAimAt(true) - mainPlayer.GetPositionForAIToAimAt(false));
                    float relativeRotationX = Mathf.DeltaAngle(mainPlayer.GetTotalTilt(), rotationXFromDirection);
                    UpdateVerticalRotationTowardsTarget(relativeRotationX, mainPlayer);
                }
            }
            catch (NullReferenceException e)
            {
                Logger.LogError("Error in GetTotalTilt: " + e.Message);
            }

            if (vector.magnitude < 7.0f && Mathf.Abs(num) < 70.0f && !nearestPlayer.HasFallenDown())
            {
                mainPlayer.SetSecondAttackKeyDown(true);
            }

            if (vector.magnitude < 5.0f)
            {
                // Логика атак
                float heightDifference = nearestPlayer.transform.position.y - mainPlayer.transform.position.y;
                float angleToTarget = Mathf.Abs(num);

/*                mainPlayer.SetUpKeyDown(false);
                mainPlayer.SetDownKeyDown(false);
                mainPlayer.SetLeftKeyDown(false);
                mainPlayer.SetRightKeyDown(false);*/

                // Проверка на вертикальную атаку (враг выше или ниже игрока)
                if (Mathf.Abs(heightDifference) > 1.0f) // Порог высоты для атаки вверх/вниз
                {
                    if (heightDifference > 0)
                    {
                        Ver = 1f;
                        mainPlayer.SetAttackKeyDown(true);
                        StartCoroutine(SetVerWithDelay(0.01f));
                    }
                    else
                    {
                        Ver = -1f;
                        mainPlayer.SetAttackKeyDown(true);
                        StartCoroutine(SetVerWithDelay(0.01f));
                    }
                }
                // Проверка на горизонтальную атаку (враг слева или справа от игрока на одном уровне)
                else if (angleToTarget <= 90) // Угол 180 градусов (90 в каждую сторону)
                {
                    if (num >= -15 && num <= 15)
                    {
                        // Враг прямо перед персонажем
                        Hor = 0f;
                        Ver = 0f;
                        mainPlayer.SetAttackKeyDown(true);
                        StartCoroutine(SetHorWithDelay(0.01f));
                        StartCoroutine(SetVerWithDelay(0.01f));
                    }
                    else if (num < 0)
                    {
                        // Враг слева
                        Hor = -1f;
                        mainPlayer.SetAttackKeyDown(true);
                        StartCoroutine(SetHorWithDelay(0.01f));
                    }
                    else
                    {
                        // Враг справа
                        Hor = 1f;
                        mainPlayer.SetAttackKeyDown(true);
                        StartCoroutine(SetHorWithDelay(0.01f));
                    }
                }
            }



            /*// Предсказание будущей позиции ближайшего игрока
            Vector3 enemyPosition = nearestPlayer.GetPositionForAIToAimAt(false);


            //Horizontal
            Vector3 directionToFuturePosition = enemyPosition - mainPlayer.transform.position;
            float targetHorizontalAngle = Mathf.Atan2(directionToFuturePosition.x, directionToFuturePosition.z) * Mathf.Rad2Deg;
            Vector3 currentEulerAngles = mainPlayer.transform.eulerAngles;
            float horizontalRotationDifference = Mathf.DeltaAngle(currentEulerAngles.y, targetHorizontalAngle);
            float smoothHorizontalRotation = Mathf.LerpAngle(currentEulerAngles.y, currentEulerAngles.y + horizontalRotationDifference, Time.deltaTime * 10f);
            mainPlayer.SetHorizontalCursorMovement(smoothHorizontalRotation - currentEulerAngles.y);


            //Vertical
            float verticalRotationSpeedFactor = -0.05f;

            if (mainPlayer != null)
            {
                try
                {
                    if (! (mainPlayer.GetEquippedWeaponType() == WeaponType.Bow))
                    {
                        float rotationXFromDirection = GetRotationXFromDirection(directionToFuturePosition);
                        float relativeRotationX = Mathf.DeltaAngle(mainPlayer.GetTotalTilt(), rotationXFromDirection);
                        
                        mainPlayer.SetVerticalCursorMovement(relativeRotationX * verticalRotationSpeedFactor);
                    }
                    else
                    {
                        float rotationXFromDirection = WorldQueries.GetRotationXFromDirection(nearestPlayer.GetPositionForAIToAimAt(true) - mainPlayer.GetPositionForAIToAimAt(false));
                        float relativeRotationX = Mathf.DeltaAngle(mainPlayer.GetTotalTilt(), rotationXFromDirection);
                        mainPlayer.SetVerticalCursorMovement(relativeRotationX * verticalRotationSpeedFactor);
                    }
                }
                catch (NullReferenceException e)
                {
                    Logger.LogError("Error in GetTotalTilt: " + e.Message);
                }
            }

            if (enemyPosition.magnitude <= 5.0f)
            {
                mainPlayer.SetUpKeyDown(true);
                mainPlayer.SetAttackKeyDown(true);
            }*/
        }


        public void UpdateRotationTowardsTarget(float relativeRotation, FirstPersonMover player)
        {
            float HorzinotalCursorMovementPerRelativeRotation = 0.04f; 
            float num2 = Time.deltaTime * 50f; 
            float num3 = HorzinotalCursorMovementPerRelativeRotation * num2;
            player.SetHorizontalCursorMovement(relativeRotation * num3);
        }

        public void UpdateVerticalRotationTowardsTarget(float relativeRotationX, FirstPersonMover player)
        {
            float VerticalCursorMovementPerRelativeRotation = -0.05f; 
            float num = 1.2f; 
            player.SetVerticalCursorMovement(relativeRotationX * VerticalCursorMovementPerRelativeRotation);
        }

        private void UpdatePlayerList()
        {
            players.Clear();
            FirstPersonMover[] characters = FindObjectsOfType<FirstPersonMover>();

            foreach (var character in characters)
            {
                if (character.name == "Mech_Player(Clone)")
                {
                    players.Add(character);
                }
            }
        }

        private void OnGUI()
        {
            if (players == null || Camera.main == null) return;

            // Display aimbot status at the top right
            GUIStyle aimbotStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                alignment = TextAnchor.UpperRight,
                normal = new GUIStyleState { textColor = isAimbotActive ? Color.green : Color.red }
            };

            string aimbotStatus = isAimbotActive ? "Aimbot: ON" : "Aimbot: OFF";
            float xPos = Screen.width - 10;
            float yPos = 10;
            Vector2 aimbotTextSize = aimbotStyle.CalcSize(new GUIContent(aimbotStatus));
            GUI.Label(new Rect(xPos - aimbotTextSize.x, yPos, aimbotTextSize.x, aimbotTextSize.y), aimbotStatus, aimbotStyle);

            // Display prediction time slider
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                normal = new GUIStyleState { textColor = Color.white }
            };
            GUI.Label(new Rect(10, 10, 200, 30), $"Prediction Time: {predictionTime:F2}s", labelStyle);
            predictionTime = GUI.HorizontalSlider(new Rect(10, 40, 200, 30), predictionTime, 0.1f, 3f);

            FirstPersonMover targetPlayer = null;
            float nearestDistance = float.MaxValue;

            // Find the nearest player and render tracers and nicknames for all players
            foreach (var player in players)
            {
                if (player == null || !player.IsAlivePlayer())
                {
                    continue;
                }

                // Check if the player is the main player
                bool isMainPlayer = player.IsMainPlayer();

                // Calculate the screen position for each player
                Vector3 screenPosition = Camera.main.WorldToScreenPoint(player.transform.position);
                if (screenPosition.z > 0) // Only render if the player is in front of the camera
                {
                    screenPosition.y = Screen.height - screenPosition.y; // Invert y-axis for GUI

                    // Display tracers from each player to the screen's bottom center
                    DrawLine(new Vector2(Screen.width / 2, Screen.height), new Vector2(screenPosition.x, screenPosition.y), Color.red);

                    // Display each player's nickname above their position
                    GUIStyle nameStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 20, 
                        alignment = TextAnchor.MiddleCenter,
                        normal = new GUIStyleState { textColor = Color.cyan }
                    };
                    GUI.Label(new Rect(screenPosition.x - 50, screenPosition.y - 40, 100, 40), GetNicknameByPlayFabID(player.GetPlayFabID()), nameStyle);

                    // Update nearest player info
                    if (!isMainPlayer)
                    {
                        float distance = Vector3.Distance(Camera.main.transform.position, player.transform.position);
                        if (distance < nearestDistance)
                        {
                            nearestDistance = distance;
                            targetPlayer = player;
                        }
                    }
                }
            }

            // Display the nearest player's name and speed at the top left
            if (targetPlayer != null)
            {
                GUIStyle targetStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 20,
                    alignment = TextAnchor.UpperLeft,
                    normal = new GUIStyleState { textColor = Color.green }
                };

                string targetName = GetNicknameByPlayFabID(targetPlayer.GetPlayFabID());
                float targetVelocity = calculatedVelocities.ContainsKey(targetPlayer)
                    ? calculatedVelocities[targetPlayer].magnitude
                    : 0.0f;

                GUI.Label(new Rect(10, 70, 200, 30), $"Target: {targetName}", targetStyle);
                GUI.Label(new Rect(10, 100, 200, 30), $"Speed: {targetVelocity:F2}", targetStyle);
            }
        }

        // Method to draw tracers
        private void DrawLine(Vector2 start, Vector2 end, Color color)
        {
            Color originalColor = GUI.color;
            GUI.color = color;
            Matrix4x4 matrix = GUI.matrix;
            float angle = Mathf.Atan2(end.y - start.y, end.x - start.x) * 180 / Mathf.PI;
            float distance = Vector3.Distance(start, end);

            GUIUtility.RotateAroundPivot(angle, start);
            GUI.DrawTexture(new Rect(start.x, start.y, distance, 2), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = originalColor;
        }

        public string GetNicknameByPlayFabID(string playFabID)
        {
            List<MultiplayerPlayerInfoState> allPlayerInfoStates = Singleton<MultiplayerPlayerInfoManager>.Instance.GetAllPlayerInfoStates();
            if (allPlayerInfoStates == null) return null;

            foreach (var playerInfoState in allPlayerInfoStates)
            {
                if (playerInfoState.state.PlayFabID == playFabID)
                {
                    return playerInfoState.state.DisplayName;
                }
            }

            return null;
        }



        private void UseAi()
        {
            FirstPersonMover mainPlayer = null;
            foreach (var character in players)
            {
                if (character != null && character.IsMainPlayer())
                {
                    mainPlayer = character;
                    break;
                }
            }

            if (mainPlayer == null)
            {
                return;
            }


            if (!mainPlayer.IsPlayerInputEnabled()) mainPlayer.EnablePlayerInput();

            if (Input.GetKeyDown(KeyCode.F7))
            {
                Hor = 1f;

                mainPlayer.SetAttackKeyDown(true);

                StartCoroutine(SetHorWithDelay(0.01f));
            }





        }

        private IEnumerator SetHorWithDelay(float delay)
        {
            yield return new WaitForSeconds(delay); // Ждем 300 мс
            Hor = 10f;
        }

        private IEnumerator SetVerWithDelay(float delay)
        {
            yield return new WaitForSeconds(delay); // Ждем 300 мс
            Ver = 10f;
        }

        private void SetPrivateField(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(obj, value);
            }
            else
            {
                Console.WriteLine($"Поле {fieldName} не найдено.");
            }
        }


        [HarmonyPatch(typeof(FirstPersonMover), "SimulateController")] // Замените YourEntityType на нужный класс
        class SimulateControllerPatch
        {
            static void Prefix(object __instance)
            {
                // Получаем значения полей через рефлексию
                var horizontalMovementField = __instance.GetType().GetField("_horizontalMovement", BindingFlags.NonPublic | BindingFlags.Instance);
                var verticalMovementField = __instance.GetType().GetField("_verticalMovement", BindingFlags.NonPublic | BindingFlags.Instance);
                var moveCommandInputField = __instance.GetType().GetField("_moveCommandInput", BindingFlags.NonPublic | BindingFlags.Instance);

                if (horizontalMovementField != null && verticalMovementField != null && moveCommandInputField != null)
                {
                    // Устанавливаем значение HorizontalMovement и VerticalMovement
                    if (Hor != 10f)
                    {
                        horizontalMovementField.SetValue(__instance, Hor);
                    }
                    if (Ver != 10f)
                    {
                        verticalMovementField.SetValue(__instance, Ver);
                    }

                    // Обновляем _moveCommandInput, чтобы отразить изменения в HorizontalMovement и VerticalMovement
                    var moveCommandInput = moveCommandInputField.GetValue(__instance);
                    if (moveCommandInput != null)
                    {
                        var horizontalMovementInputField = moveCommandInput.GetType().GetField("HorizontalMovement", BindingFlags.Public | BindingFlags.Instance);
                        var verticalMovementInputField = moveCommandInput.GetType().GetField("VerticalMovement", BindingFlags.Public | BindingFlags.Instance);

                        if (horizontalMovementInputField != null && verticalMovementInputField != null)
                        {
                            // Устанавливаем значения для _moveCommandInput на основе Hor и Ver
                            horizontalMovementInputField.SetValue(moveCommandInput, Hor != 10f ? Hor : horizontalMovementField.GetValue(__instance));
                            verticalMovementInputField.SetValue(moveCommandInput, Ver != 10f ? Ver : verticalMovementField.GetValue(__instance));
                        }
                    }
                }
            }
    }



    }
}
