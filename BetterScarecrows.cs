using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Better Scarecrows", "Spiikesan", "1.2.1")]
    [Description("Fix and improve scarecrows")]
    public class BetterScarecrows : RustPlugin
    {
        const string b64ScarecrowDesign = "CAEIAwgPCBoIGwgcCB0SUggAEAEaFAgBEAEYACAAKAAwAKoGBQ0AACBBGhkIAxAFGAAgBCgAMAGiBgoNAAAAABUAAAAAGhkIABAGGAAgACgAMAKiBgoNAAAAQRUAAHBBIAASPQgBEAMaDAgFEAIYACAAKAAwABoMCBQQABgAIAAoADABGhkIAxAFGAAgBCgAMAKiBgoNAAAAABUAAAAAIAASRQgCEA8aDAgUEAAYACAAKAAwABoMCAUQARgBIAAoADABGiEIExAEGAAgACgAMASiBgoNAAAAABUAAAAA6gYFDc3MzD0gABJ6CAMQGhoZCAIQABgAIAAoADABogYKDQAAAAAVAAAAABoZCAQQABgAIAAoADACogYKDQAAAAAVAAAAABohCAEQARgAIAAoADADogYKDQAAAAAVAAAAAKoGBQ0AACBBGhkIAxAFGAAgBCgAMASiBgoNAAAAABUAAAAAIAASPAgEEBsaGQgCEAAYACAAKAAwAaIGCg0AAAAAFQAAAAAaGQgEEAEYACAAKAAwAqIGCg0AAAAAFQAAAAAgABI8CAUQHBoZCAIQARgAIAAoADABogYKDQAAAAAVAAAAABoZCAQQABgAIAAoADACogYKDQAAAAAVAAAAACAAEjwIBhAdGhkIAhAAGAAgACgAMAGiBgoNAAAAABUAAAAAGhkIBBADGAAgACgAMAKiBgoNAAAAABUAAAAAIAAYACIQQmV0dGVyIHNjYXJlY3JvdygBMAA=";

        const float SOUND_DELAY = 3f;

        static AIState _lastAIStateEnumValue = Enum.GetValues(typeof(AIState)).Cast<AIState>().Max() + 1;
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

            [JsonProperty("Breathing")]
            public string Breathing = "assets/prefabs/npc/murderer/sound/breathing.prefab";
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
            if (_customDesign == null)
            {
                PrintError("The custom design could not be loaded !");
                Unload();
            }
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
            entity.InitializeHealth(_config.Health, _config.Health);
            NextTick(() =>
            {
                updateEntityBrain(entity, false);
            });
        }

        private void OnEntityDeath(ScarecrowNPC entity)
        {
            Effect.server.Run(_config.Sounds.Death, entity, 0, Vector3.zero, entity.eyes.transform.forward.normalized);
        }

        #endregion

        #region Helpers
        static AIState GetAIState(AICustomState state) => (AIState)((int)_lastAIStateEnumValue + (int)state);

        static AICustomState GetAICustomState(AIState state) => (AICustomState)((int)state - (int)_lastAIStateEnumValue);

        static bool IsCustomState(AIState state) => state >= _lastAIStateEnumValue;

        public void updateAllScarecrows(bool revert)
        {
            ScarecrowNPC[] baseEntityArray = BaseNetworkable.serverEntities.OfType<ScarecrowNPC>().ToArray();
            if (baseEntityArray != null)
            {
                foreach (ScarecrowNPC entity in baseEntityArray)
                    updateEntityBrain(entity, revert);
            }
        }

        private void updateEntityBrain(ScarecrowNPC entity, bool revert)
        {
            if (entity != null && !entity.IsDestroyed)
            {
                entity.Brain.AttackRangeMultiplier = _config.AttackRangeMultiplier;
                entity.Brain.TargetLostRange = _config.TargetLostRange;
                entity.Brain.SenseRange = _config.SenseRange;
                if (!revert)
                {
                    if (!entity.gameObject.HasComponent<ScarecrowSounds>())
                        entity.gameObject.AddComponent<ScarecrowSounds>();
                    if (!entity.Brain.states.ContainsKey(GetAIState(AICustomState.RoamState)))
                        entity.Brain.AddState(new RoamState());
                    if (!entity.Brain.states.ContainsKey(GetAIState(AICustomState.ThrowGrenadeState)))
                        entity.Brain.AddState(new ThrowGrenadeState());
                    if (!entity.Brain.states.ContainsKey(GetAIState(AICustomState.FleeInhuman)))
                        entity.Brain.AddState(new FleeInhuman());
                    if (!entity.Brain.states.ContainsKey(GetAIState(AICustomState.Awaken)))
                        entity.Brain.AddState(new Awaken());
                    entity.Brain.InstanceSpecificDesign = _customDesign;
                }
                else
                {
                    entity.Brain.InstanceSpecificDesign = null;
                    if (entity.gameObject.HasComponent<ScarecrowSounds>())
                        UnityEngine.Object.Destroy(entity.gameObject.GetComponent<ScarecrowSounds>());
                }
                entity.Brain.LoadAIDesignAtIndex(entity.Brain.LoadedDesignIndex());
            }
        }

        #endregion

        #region Custom states
        private class RoamState : BaseAIBrain<ScarecrowNPC>.BasicAIState
        {
            private StateStatus status = StateStatus.Error;
            public RoamState() : base(GetAIState(AICustomState.RoamState))
            {
            }

            public override void StateEnter()
            {
                Vector3 bestRoamPosition;
                base.StateEnter();
                status = StateStatus.Error;
                if (brain.PathFinder != null)
                {
                    if (!brain.InGroup() || brain.IsGroupLeader)
                    {
                        bestRoamPosition = brain.PathFinder.GetBestRoamPosition(brain.Navigator, brain.Events.Memory.Position.Get(4), 20f, 100f);
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

            public override void StateLeave()
            {
                base.StateLeave();
                brain.Navigator.Stop();
            }

            public override StateStatus StateThink(float delta)
            {
                base.StateThink(delta);
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

        private class ThrowGrenadeState : BaseAIBrain<ScarecrowNPC>.BasicAIState
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
                    _entity = GetEntity() as NPCPlayer;
                    _grenade = _entity.inventory.containerBelt.GetSlot(1);
                    _target = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot) as BasePlayer;
                    canEnter = base.CanEnter() && _grenade != null && _target != null;
                }
                return canEnter;
            }

            public override void StateEnter()
            {
                base.StateEnter();
                _entity.UpdateActiveItem(_grenade.uid);
                _isThrown = false;
            }

            public override StateStatus StateThink(float delta)
            {
                base.StateThink(delta);
                _target = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot) as BasePlayer;
                StateStatus status = StateStatus.Error;

                if (_target != null)
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

            public override void StateLeave()
            {
                base.StateLeave();
                _entity.UpdateActiveItem(_entity.inventory.containerBelt.GetSlot(0).uid);
            }
        }

        public class FleeInhuman : BaseAIBrain<ScarecrowNPC>.BasicAIState
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

            public override void StateEnter()
            {
                base.StateEnter();
                BaseEntity baseEntity = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot);
                if (baseEntity != null && !(baseEntity is BasePlayer))
                {
                    stopFleeDistance = UnityEngine.Random.Range(5f, 10f);
                    FleeFrom(baseEntity, GetEntity());
                }
            }

            public override bool CanLeave()
            {
                BaseEntity baseEntity = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot);
                return base.CanLeave() && ( baseEntity == null || baseEntity is BasePlayer || Vector3Ex.Distance2D(brain.Navigator.transform.position, baseEntity.transform.position) >= stopFleeDistance );
            }

            public override void StateLeave()
            {
                base.StateLeave();
                Stop();
            }

            public override StateStatus StateThink(float delta)
            {
                base.StateThink(delta);
                BaseEntity baseEntity = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot);
                if (baseEntity == null)
                {
                    return StateStatus.Finished;
                }
                else if (baseEntity is BasePlayer)
                {
                    return StateStatus.Error;
                }
                if (Vector3Ex.Distance2D(brain.Navigator.transform.position, baseEntity.transform.position) >= stopFleeDistance)
                {
                    return StateStatus.Finished;
                }
                if (!brain.Navigator.UpdateIntervalElapsed(nextInterval) && brain.Navigator.Moving || FleeFrom(baseEntity, GetEntity()))
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

        public class Awaken : BaseAIBrain<ScarecrowNPC>.BasicAIState
        {
            BasePlayer[] players = new BasePlayer[1];

            public Awaken() : base(GetAIState(AICustomState.Awaken))
            {
            }

            public override StateStatus StateThink(float delta)
            {
                StateStatus status = StateStatus.Finished;
                if (Rust.Ai.AiManager.ai_dormant
                    && BaseEntity.Query.Server.GetPlayersInSphere(GetEntity().transform.position, Rust.Ai.AiManager.ai_to_player_distance_wakeup_range, players, (p) => p.userID.IsSteamId()) == 0)
                {
                    status = StateStatus.Error;
                }
                players[0] = null;
                return status;
            }
        }

        #endregion

        #region Sound management

        private class ScarecrowSounds : FacepunchBehaviour
        {
            public ScarecrowNPC Scarecrow { get; private set; }

            private Dictionary<AIState, Sound> sounds = new Dictionary<AIState, Sound>();
            Sound lastSound;

            public void Awake()
            {
                Scarecrow = GetComponent<ScarecrowNPC>();

                Sound breathingSound = new Sound(_instance._config.Sounds.Breathing, 1f, 10f, 0.8f);

                sounds.Add(AIState.Chase, breathingSound);
                sounds.Add(AIState.Attack, breathingSound);
            }

            public void Update()
            {
                Sound sound;

                if (sounds.TryGetValue(Scarecrow.Brain.CurrentState.StateType, out sound))
                {
                    sound.TryExecute(Scarecrow, Time.deltaTime);

                    if (lastSound != sound)
                    {
                        lastSound = sound;
                    }
                }
            }

            private class Sound
            {
                public string SoundName { get; private set; }
                public float StartDelay { get; private set; }
                public float MinDelay { get; private set; }
                public float Chance { get; private set; }

                private float currentDelay;

                public Sound(string soundName, float startDelay, float minDelay, float chance)
                {
                    SoundName = soundName;
                    StartDelay = startDelay;
                    MinDelay = minDelay;
                    Chance = chance > 1f ? 1f : chance < 0f ? 0f : chance;
                    Reset();
                }

                public void TryExecute(ScarecrowNPC entity, float deltaTime)
                {
                    currentDelay += deltaTime;
                    bool runSound = ((currentDelay >= MinDelay)
                                    && (!(1.0f - Chance > 0.0)
                                        || UnityEngine.Random.Range(0f, 1f) < Chance));
                    if (runSound)
                    {
                        Effect.server.Run(SoundName, entity, 0, Vector3.zero, entity.eyes.transform.forward.normalized);
                        currentDelay = 0f;
                    }
                }

                public void Reset()
                {
                    currentDelay = MinDelay - StartDelay;
                }
            }
        }
        #endregion
    }
}
