using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using ProtoBuf;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Better Scarecrows", "Spiikesan", "1.5.9")]
    [Description("Fix and improve scarecrows")]
    public class BetterScarecrows : RustPlugin
    {
        const string b64ScarecrowDesign = "CAEIAwgPCBwIHQgeCB8SUggAEAEaFAgBEAEYACAAKAAwAKoGBQ0AACBBGhkIAxAFGAAgBCgAMAGiBgoNAAAAABUAAAAAGhkIABAGGAAgACgAMAKiBgoNAAAAQRUAAHBBIAASPQgBEAMaDAgFEAIYACAAKAAwABoMCBQQABgAIAAoADABGhkIAxAFGAAgBCgAMAKiBgoNAAAAABUAAAAAIAASRQgCEA8aDAgUEAAYACAAKAAwABoMCAUQARgBIAAoADABGiEIExAEGAAgACgAMASiBgoNAAAAABUAAAAA6gYFDc3MzD0gABJ6CAMQHBoZCAIQABgAIAAoADABogYKDQAAAAAVAAAAABoZCAQQABgAIAAoADACogYKDQAAAAAVAAAAABohCAEQARgAIAAoADADogYKDQAAAAAVAAAAAKoGBQ0AACBBGhkIAxAFGAAgBCgAMASiBgoNAAAAABUAAAAAIAASPAgEEB0aGQgCEAAYACAAKAAwAaIGCg0AAAAAFQAAAAAaGQgEEAEYACAAKAAwAqIGCg0AAAAAFQAAAAAgABI8CAUQHhoZCAIQARgAIAAoADABogYKDQAAAAAVAAAAABoZCAQQABgAIAAoADACogYKDQAAAAAVAAAAACAAEjwIBhAfGhkIAhAAGAAgACgAMAGiBgoNAAAAABUAAAAAGhkIBBADGAAgACgAMAKiBgoNAAAAABUAAAAAIAAYACIQQmV0dGVyIHNjYXJlY3JvdygBMAA=";

        const float SOUND_DELAY = 3f;

        static AIState _lastAIStateEnumValue = AIState.Blinded;
        static AIState _maxAIStateEnumValue = Enum.GetValues(typeof(AIState)).Cast<AIState>().Max();

        static BetterScarecrows _instance;

        enum AICustomState
        {
            UnusedState, //For compatibility with my AIManager plugin (used to create or update the content of the b64ScarecrowDesign) - Do not remove
            RoamState,
            ThrowGrenadeState,
            FleeInhuman,
            Awaken,
            // Maybe more states in the future ?
        };

        ProtoBuf.AIDesign _customDesign;

        #region Configuration

        private ScarecrowConfiguration _config;
        private ConVars _previousConVars;

        public class ConVars
        {
            [JsonProperty("OverrideConVars")]
            public bool OverrideConVars = false;

            [JsonProperty("ScarecrowPopulation")]
            public float ScarecrowPopulation = 5.0f;

            [JsonProperty("scarecrowsThrowBeancans")]
            public bool ScarecrowsThrowBeancans = true;

            [JsonProperty("scarecrowThrowBeancanGlobalDelay")]
            public float ScarecrowThrowBeancanGlobalDelay = 8.0f;
        }

        public class Sounds
        {
            [JsonProperty("Death")]
            public string Death = "assets/prefabs/npc/murderer/sound/death.prefab";
        }

        public class ScarecrowConfiguration
        {
            [JsonProperty("Health")]
            public float Health = 250.0f;

            [JsonProperty("AttackRangeMultiplier")]
            public float AttackRangeMultiplier = 0.75f;

            [JsonProperty("TargetLostRange")]
            public float TargetLostRange = 20f;

            [JsonProperty("SenseRange")]
            public float SenseRange = 15f;

            [JsonProperty("WalkSpeedFraction")]
            public float WalkSpeed = 0.3f;

            [JsonProperty("RunSpeedFraction")]
            public float RunSpeed = 1f;

            [JsonProperty("IgnoreSafeZonePlayers")]
            public bool IgnoreSafeZonePlayers = true;

            [JsonProperty("CanBradleyAPCTargetScarecrow")]
            public bool CanBradleyAPCTargetScarecrow = true;

            [JsonProperty("CanNPCTurretsTargetScarecrow")]
            public bool CanNPCTurretsTargetScarecrow = true;

            [JsonProperty("CanNPCScientistsTargetScarecrow")]
            public bool CanNPCScientistsTargetScarecrow = true;

            [JsonProperty("CanScarecrowTargetNPCScientists")]
            public bool CanScarecrowTargetNPCScientists = true;

            [JsonProperty("CanNPCBanditGuardTargetScarecrow")]
            public bool CanNPCBanditGuardTargetScarecrow = true;

            [JsonProperty("CanScarecrowTargetNPCBanditGuard")]
            public bool CanScarecrowTargetNPCBanditGuard = true;

            [JsonProperty("DisableLoot")]
            public bool DisableLoot = false;

            [JsonProperty("UseCustomAI")]
            public bool UseCustomAI = true;

            public Sounds Sounds = new Sounds();

            [JsonProperty("ConVars")]
            public ConVars ConVars = new ConVars();

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        };

        protected override void LoadDefaultConfig() => _config = new ScarecrowConfiguration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<ScarecrowConfiguration>();
                if (_config == null)
                {
                    throw new JsonException();
                }

                if (!_config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    PrintWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                PrintWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            PrintWarning($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_config, true);
        }

        #endregion

        #region Oxide hooks
        void Init()
        {
            _instance = this;
            _customDesign = ProtoBuf.AIDesign.Deserialize(Convert.FromBase64String(b64ScarecrowDesign));
            if (_lastAIStateEnumValue != _maxAIStateEnumValue)
            {
                PrintWarning($"{_maxAIStateEnumValue - _lastAIStateEnumValue} new state(s) have been added by Facepunch. An update of the AI is required !");
                computeNewDesign(_maxAIStateEnumValue - _lastAIStateEnumValue);
            }
            if (_customDesign == null)
            {
                PrintError("The custom design could not be loaded !");
                Unload();
            }
        }

        private void computeNewDesign(int offset)
        {
            //Updating availableStates.
            for (int i = 0; i < _customDesign.availableStates.Count; i++)
            {
                if (_customDesign.availableStates[i] > (int)_lastAIStateEnumValue)
                {
                    _customDesign.availableStates[i] += offset;
                }
            }
            //Updating state container
            for (int i = 0; i < _customDesign.stateContainers.Count; i++)
            {
                if (_customDesign.stateContainers[i].state > (int)_lastAIStateEnumValue)
                {
                    _customDesign.stateContainers[i].state += offset;
                }
            }
            PrintWarning("AI is updated with the new values. An update of the plugin is probably already pending from the plugin developer, but the plugin will continue to work.");
        }

        void OnServerInitialized()
        {
            updateAllScarecrows(false);

            if (_config.ConVars.OverrideConVars)
            {
                _previousConVars = new ConVars()
                {
                    OverrideConVars = true,
                    ScarecrowPopulation = ConVar.Halloween.scarecrowpopulation,
                    ScarecrowsThrowBeancans = ConVar.Halloween.scarecrows_throw_beancans,
                    ScarecrowThrowBeancanGlobalDelay = ConVar.Halloween.scarecrow_throw_beancan_global_delay
                };
                ConVar.Halloween.scarecrowpopulation = _config.ConVars.ScarecrowPopulation;
                ConVar.Halloween.scarecrows_throw_beancans = _config.ConVars.ScarecrowsThrowBeancans;
                ConVar.Halloween.scarecrow_throw_beancan_global_delay = _config.ConVars.ScarecrowThrowBeancanGlobalDelay;
            }
        }

        void Unload()
        {
            updateAllScarecrows(true);
            if (_config.ConVars.OverrideConVars && _previousConVars != null)
            {
                ConVar.Halloween.scarecrowpopulation = _previousConVars.ScarecrowPopulation;
                ConVar.Halloween.scarecrows_throw_beancans = _previousConVars.ScarecrowsThrowBeancans;
                ConVar.Halloween.scarecrow_throw_beancan_global_delay = _previousConVars.ScarecrowThrowBeancanGlobalDelay;
            }
            _instance = null;
        }

        private void OnEntitySpawned(ScarecrowNPC entity)
        {
            // The brain is hooked on the next frame.
            if (entity != null
                && !entity.IsDestroyed)
            {
                NextTick(() =>
                {
                    if (entity.Brain != null)
                    {
                        UpdateScarecrowConfiguration(entity, false);
                    }
                });
            }
        }


        private void OnEntityDeath(ScarecrowNPC entity)
        {
            Effect.server.Run(_config.Sounds.Death, entity, 0, Vector3.zero, entity.eyes.transform.forward.normalized);
        }

        private object CanBradleyApcTarget(BradleyAPC bradley, ScarecrowNPC scarecrow)
        {
            if (!_config.CanBradleyAPCTargetScarecrow)
                return false;
            return null;
        }

        private object CanBeTargeted(ScarecrowNPC scarecrow, NPCAutoTurret turret)
        {
            if (!_config.CanNPCTurretsTargetScarecrow)
                return false;
            return null;
        }

        private object OnNpcTarget(BaseEntity npc, BaseEntity entity)
        {
            // ScarecrowNPC is targeted.
            if (entity is ScarecrowNPC)
            {
                if (npc is ScientistNPC && !_config.CanNPCScientistsTargetScarecrow
                    || npc is BanditGuard && !_config.CanNPCBanditGuardTargetScarecrow)
                {
                    return true;
                }
            }
            // ScarecrowNPC is targeting.
            if (npc is ScarecrowNPC)
            {
                if (entity is ScientistNPC && !_config.CanScarecrowTargetNPCScientists
                    || entity is BanditGuard && !_config.CanScarecrowTargetNPCBanditGuard)
                {
                    return true;
                }
            }
            return null;
        }

        private object OnCorpsePopulate(ScarecrowNPC scarecrow, NPCPlayerCorpse corpse)
        {
            return _config.DisableLoot ? corpse : null;
        }

        #endregion

        #region Helpers
        static AIState GetAIState(AICustomState state) => (AIState)((int)_maxAIStateEnumValue + 1 + (int)state);

        static AICustomState GetAICustomState(AIState state) => (AICustomState)((int)state - (int)(_maxAIStateEnumValue + 1));

        static bool IsCustomState(AIState state) => state > _maxAIStateEnumValue;

        static void TraceLog(string format, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string caller = null) => _instance.Puts("(" + caller + ":" + lineNumber + ") " + format);

        public void updateAllScarecrows(bool revert)
        {
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                ScarecrowNPC scarecrow = entity as ScarecrowNPC;
                if (scarecrow != null && !scarecrow.IsDestroyed)
                {
                    if (scarecrow.Brain != null)
                    {
                        UpdateScarecrowConfiguration(scarecrow, revert);
                    }
                    else
                    {
                        // If the scarecrow just spawned, his brain will only be there the next tick.
                        NextTick(() =>
                        {
                            if (scarecrow.Brain != null)
                            {
                                UpdateScarecrowConfiguration(scarecrow, revert);
                            }
                        });
                    }
                }
            }
        }

        private void UpdateScarecrowConfiguration(ScarecrowNPC entity, bool revert)
        {
            entity.InitializeHealth(_config.Health, _config.Health);
            entity.Brain.AttackRangeMultiplier = _config.AttackRangeMultiplier;
            entity.Brain.TargetLostRange = _config.TargetLostRange;
            entity.Brain.SenseRange = _config.SenseRange;
            entity.Brain.Senses.ignoreSafeZonePlayers = _config.IgnoreSafeZonePlayers;
            entity.Brain.Navigator.SlowSpeedFraction = _config.WalkSpeed;
            entity.Brain.Navigator.FastSpeedFraction = _config.RunSpeed;

            if (_config.UseCustomAI)
            {
                if (entity.Brain.states == null)
                    entity.Brain.AddStates();
                if (!revert)
                {
                    if (!entity.Brain.states.ContainsKey(GetAIState(AICustomState.RoamState)))
                        entity.Brain.AddState(new RoamState());
                    if (!entity.Brain.states.ContainsKey(GetAIState(AICustomState.ThrowGrenadeState)))
                        entity.Brain.AddState(new ThrowGrenadeState());
                    if (!entity.Brain.states.ContainsKey(GetAIState(AICustomState.FleeInhuman)))
                        entity.Brain.AddState(new FleeInhuman());
                    if (!entity.Brain.states.ContainsKey(GetAIState(AICustomState.Awaken)))
                        entity.Brain.AddState(new Awaken());
                    entity.Brain.states[AIState.Attack] = new AttackState();
                    entity.Brain.states[AIState.Attack].brain = entity.Brain;
                    entity.Brain.states[AIState.Attack].Reset();
                    entity.Brain.InstanceSpecificDesign = _customDesign;
                }
                else
                {
                    entity.Brain.states[AIState.Attack] = new ScarecrowBrain.AttackState();
                    entity.Brain.states[AIState.Attack].brain = entity.Brain;
                    entity.Brain.states[AIState.Attack].Reset();
                    entity.Brain.InstanceSpecificDesign = null;
                }
            }
            entity.Brain.LoadAIDesignAtIndex(entity.Brain.LoadedDesignIndex());
        }

        #endregion

        #region AI States
        public class AttackState : BaseAIBrain.BasicAIState
        {
            private IAIAttack attack;

            Chainsaw chainsaw;

            public AttackState() : base(AIState.Attack)
            {
                AgrresiveState = true;
            }

            private static Vector3 GetAimDirection(Vector3 from, Vector3 target)
            {
                return Vector3Ex.Direction2D(target, from);
            }

            private void StartAttacking(BaseEntity entity)
            {
                attack.StartAttacking(entity);
                if (chainsaw != null)
                {
                    chainsaw.ServerNPCStart();
                    chainsaw.SetFlag(BaseEntity.Flags.Busy, true, false, true);
                    chainsaw.SetFlag(BaseEntity.Flags.Reserved8, true, false, true);
                }
            }

            public override void StateEnter(BaseAIBrain brain, BaseEntity entity)
            {
                base.StateEnter(brain, entity);
                ScarecrowNPC scarecrow = entity as ScarecrowNPC;
                attack = entity as IAIAttack;
                chainsaw = scarecrow.GetHeldEntity() as Chainsaw;
                BaseEntity baseEntity = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot);
                if (baseEntity != null)
                {
                    Vector3 aimDirection = GetAimDirection(brain.Navigator.transform.position, baseEntity.transform.position);
                    brain.Navigator.SetFacingDirectionOverride(aimDirection);
                    if (attack.CanAttack(baseEntity))
                    {
                        StartAttacking(baseEntity);
                    }
                    brain.Navigator.SetDestination(baseEntity.transform.position, BaseNavigator.NavigationSpeed.Fast, 0f, 0f);
                }
            }

            public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
            {
                base.StateLeave(brain, entity);
                brain.Navigator.ClearFacingDirectionOverride();
                brain.Navigator.Stop();
                StopAttacking();
            }

            public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
            {
                base.StateThink(delta, brain, entity);
                BaseEntity baseEntity = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot);
                if (attack == null)
                {
                    return StateStatus.Error;
                }
                if (baseEntity == null)
                {
                    brain.Navigator.ClearFacingDirectionOverride();
                    StopAttacking();
                    return StateStatus.Finished;
                }
                if (brain.Senses.ignoreSafeZonePlayers)
                {
                    BasePlayer basePlayer = baseEntity as BasePlayer;
                    if (basePlayer != null && basePlayer.InSafeZone())
                    {
                        return StateStatus.Error;
                    }
                }
                if (!brain.Navigator.SetDestination(baseEntity.transform.position, BaseNavigator.NavigationSpeed.Fast, 0.25f, 0f))
                {
                    return StateStatus.Error;
                }
                Vector3 aimDirection = GetAimDirection(brain.Navigator.transform.position, baseEntity.transform.position);
                brain.Navigator.SetFacingDirectionOverride(aimDirection);
                if (!attack.CanAttack(baseEntity))
                {
                    StopAttacking();
                }
                else
                {
                    StartAttacking(baseEntity);
                }
                return StateStatus.Running;
            }

            private void StopAttacking()
            {
                attack.StopAttacking();
                if (chainsaw != null)
                {
                    chainsaw.SetFlag(BaseEntity.Flags.Busy, false, false, true);
                    chainsaw.SetFlag(BaseEntity.Flags.Reserved8, false, false, true);
                }
            }
        }
        #endregion
        #region AI Custom states

        private class RoamState : BaseAIBrain.BasicAIState
        {
            private StateStatus status = StateStatus.Error;
            public RoamState() : base(GetAIState(AICustomState.RoamState))
            {
            }

            public override void StateEnter(BaseAIBrain brain, BaseEntity entity)
            {
                Vector3 bestRoamPosition;
                base.StateEnter(brain, entity);
                status = StateStatus.Error;
                if (brain.PathFinder != null)
                {
                    if (!brain.InGroup() || brain.IsGroupLeader)
                    {
                        bestRoamPosition = brain.PathFinder.GetBestRoamPosition(brain.Navigator, brain.Navigator.transform.position, brain.Events.Memory.Position.Get(4), 20f, 100f);
                    }
                    else
                    {
                        bestRoamPosition = BasePathFinder.GetPointOnCircle(brain.Events.Memory.Position.Get(5), Core.Random.Range(2f, 7f), Core.Random.Range(0f, 359f));
                    }
                    if (brain.Navigator.SetDestination(bestRoamPosition, BaseNavigator.NavigationSpeed.Slow, 0f, 0f))
                    {
                        if (brain.InGroup() && brain.IsGroupLeader)
                        {
                            brain.SetGroupRoamRootPosition(bestRoamPosition);
                        }
                        status = StateStatus.Running;
                    }
                }
            }

            public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
            {
                base.StateLeave(brain, entity);
                brain.Navigator.Stop();
            }

            public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
            {
                base.StateThink(delta, brain, entity);
                if (status == StateStatus.Error)
                {
                    return status;
                }
                if (brain.Navigator.Moving)
                {
                    return StateStatus.Running;
                }
                return StateStatus.Finished;
            }
        }

        private class ThrowGrenadeState : BaseAIBrain.BasicAIState
        {
            bool _isThrown;
            NPCPlayer _entity = null;
            BasePlayer _target = null;
            Item _grenade = null;
            const float _MAX_DISTANCE = 5f;
            const float _THROW_TIME = 1.5f;

            public ThrowGrenadeState() : base(GetAIState(AICustomState.ThrowGrenadeState))
            {
                AgrresiveState = true;
            }

            public override bool CanEnter()
            {
                bool canEnter = false;

                if (ConVar.Halloween.scarecrows_throw_beancans && TimeSinceState() >= ConVar.Halloween.scarecrow_throw_beancan_global_delay)
                {
                    _target = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot) as BasePlayer;
                    canEnter = base.CanEnter() && _target != null && (!brain.Senses.ignoreSafeZonePlayers || !_target.InSafeZone());
                }
                return canEnter;
            }

            public override void StateEnter(BaseAIBrain brain, BaseEntity entity)
            {
                base.StateEnter(brain, entity);
                _entity = entity as NPCPlayer;
                _grenade = _entity.inventory.containerBelt.GetSlot(1);
                if (_grenade != null)
                    _entity.UpdateActiveItem(_grenade.uid);
                _isThrown = false;
            }

            public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
            {
                base.StateThink(delta, brain, entity);
                _target = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot) as BasePlayer;
                StateStatus status = StateStatus.Error;

                if (_target != null && _grenade != null)
                {
                    float distance = Vector3.Distance(_entity.transform.position, _target.transform.position);
                    if (distance < _MAX_DISTANCE)
                    {
                        status = StateStatus.Running;
                        _entity.SetAimDirection((_target.ServerPosition - _entity.ServerPosition).normalized);

                        if (!_isThrown && TimeInState >= _THROW_TIME)
                        {
                            _entity.SignalBroadcast(BaseEntity.Signal.Throw);
                            (_grenade.GetHeldEntity() as ThrownWeapon)?.ServerThrow(_target.transform.position);
                            _isThrown = true;
                        }
                        else if (TimeInState >= _THROW_TIME + 1f)
                        {
                            status = StateStatus.Finished;
                        }
                    }
                }
                return status;
            }

            public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
            {
                base.StateLeave(brain, entity);
                if (_entity.inventory.containerBelt.GetSlot(0) != null)
                    _entity.UpdateActiveItem(_entity.inventory.containerBelt.GetSlot(0).uid);
            }
        }

        public class FleeInhuman : BaseAIBrain.BasicAIState
        {
            private float nextInterval;
            private float stopFleeDistance;

            public FleeInhuman() : base(GetAIState(AICustomState.FleeInhuman))
            {

            }

            private bool FleeFrom(BaseEntity fleeFromEntity, BaseEntity thisEntity)
            {
                Vector3 vector3;
                if (thisEntity == null || fleeFromEntity == null)
                {
                    return false;
                }
                nextInterval = UnityEngine.Random.Range(3f, 6f);
                if (!brain.PathFinder.GetBestFleePosition(brain.Navigator, brain.Senses, fleeFromEntity, brain.Events.Memory.Position.Get(4), 50f, 100f, out vector3))
                {
                    return false;
                }
                bool flag = brain.Navigator.SetDestination(vector3, BaseNavigator.NavigationSpeed.Fast, 0f, 0f);
                if (!flag)
                {
                    Stop();
                }
                return flag;
            }

            public override void StateEnter(BaseAIBrain brain, BaseEntity entity)
            {
                base.StateEnter(brain, entity);
                BaseEntity baseEntity = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot);
                if (baseEntity != null && !(baseEntity is BasePlayer))
                {
                    stopFleeDistance = UnityEngine.Random.Range(5f, 10f);
                    FleeFrom(baseEntity, entity);
                }
            }

            public override bool CanLeave()
            {
                BaseEntity baseEntity = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot);
                return base.CanLeave() && (baseEntity == null || baseEntity is BasePlayer || Vector3Ex.Distance2D(brain.Navigator.transform.position, baseEntity.transform.position) >= stopFleeDistance);
            }

            public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
            {
                base.StateLeave(brain, entity);
                Stop();
            }

            public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
            {
                base.StateThink(delta, brain, entity);
                BaseEntity baseEntity = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot);
                if (baseEntity == null)
                {
                    return StateStatus.Finished;
                }
                else if (baseEntity is BasePlayer && !(baseEntity is NPCPlayer))
                {
                    return StateStatus.Error;
                }
                if (Vector3Ex.Distance2D(brain.Navigator.transform.position, baseEntity.transform.position) >= stopFleeDistance)
                {
                    return StateStatus.Finished;
                }
                if (!brain.Navigator.UpdateIntervalElapsed(nextInterval) && brain.Navigator.Moving || FleeFrom(baseEntity, entity))
                {
                    return StateStatus.Running;
                }
                return StateStatus.Error;
            }

            private void Stop()
            {
                brain.Navigator.Stop();
            }
        }

        public class Awaken : BaseAIBrain.BasicAIState
        {
            BasePlayer[] players = new BasePlayer[1];

            public Awaken() : base(GetAIState(AICustomState.Awaken))
            {
            }

            public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
            {
                StateStatus status = StateStatus.Finished;
                if (Rust.Ai.AiManager.ai_dormant
                    && BaseEntity.Query.Server.GetPlayersInSphere(entity.transform.position, Rust.Ai.AiManager.ai_to_player_distance_wakeup_range, players, (p) => p.userID.IsSteamId()) == 0)
                {
                    status = StateStatus.Error;
                }
                players[0] = null;
                return status;
            }
        }

        #endregion
    }
}