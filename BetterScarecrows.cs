using System;
using System.Linq;
using System.Runtime.CompilerServices;
using ProtoBuf;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Better Scarecrows", "Spiikesan", "0.1.0")]
    [Description("Fix and improve scarecrows")]
    public class BetterScarecrows : RustPlugin
    {
        static AIState _lastAIStateEnumValue = Enum.GetValues(typeof(AIState)).Cast<AIState>().Max() + 1;
        ProtoBuf.AIDesign _customDesign;
        static BetterScarecrows _instance;
        const string b64ScarecrowDesign = "CAEIAwgPCBoIGxJSCAAQARoUCAEQARgAIAAoADAAqgYFDQAAIEEaGQgDEAEYACAEKAAwAaIGCg0AAAAAFQAAAAAaGQgAEAMYACAAKAAwAqIGCg0AAEBAFQAAoEAgABIiCAEQAxoMCAUQAhgAIAAoADAAGgwIFBAAGAAgACgAMAEgABJFCAIQDxoMCBQQABgAIAAoADAAGgwIBRABGAEgACgAMAEaIQgTEAQYACAAKAAwBKIGCg0AAAAAFQAAAADqBgUNzczMPSAAEnoIAxAaGhkIAhAAGAAgACgAMAGiBgoNAAAAABUAAAAAGhkIBBAAGAAgACgAMAKiBgoNAAAAABUAAAAAGiEIARABGAAgACgAMAOiBgoNAAAAABUAAAAAqgYFDQAAIEEaGQgDEAEYACAEKAAwBKIGCg0AAAAAFQAAAAAgABI8CAQQGxoZCAIQABgAIAAoADABogYKDQAAAAAVAAAAABoZCAQQABgAIAAoADACogYKDQAAAAAVAAAAACAAGAAiEEJldHRlciBzY2FyZWNyb3coADAA";
        enum AICustomState
        {
            UnusedState, //For compatibility with my AIManager plugin (used to create or update the content of the b64ScarecrowDesign) - Do not remove
            RoamState,
            ThrowGrenadeState
            // Maybe more states in the future ?
        };

        #region Oxide hooks
        void Init()
        {
            _instance = this;
            _customDesign = ProtoBuf.AIDesign.Deserialize(Convert.FromBase64String(b64ScarecrowDesign));
            if(_customDesign == null)
            {
                PrintError("The custom design could not be loaded !");
                Unload();
            }
        }

        void OnServerInitialized()
        {
            updateAllScarecrows(false);
        }

        void Unload()
        {
            updateAllScarecrows(true);
            _instance = null;
        }

        private void OnEntitySpawned(ScarecrowNPC entity)
        {
            entity.InitializeHealth(250, 250);
            NextTick(() => {
                updateEntityBrain(entity, false);
            });
        }
        #endregion

        #region Helpers
        static AIState GetStateId(AICustomState state) => (AIState)((int)_lastAIStateEnumValue + (int)state);

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
            entity.Brain.AttackRangeMultiplier = 0.75f;
            entity.Brain.TargetLostRange = 20f;
            entity.Brain.SenseRange = 15f;
            if (!revert)
            {
                if (!entity.Brain.states.ContainsKey(GetStateId(AICustomState.RoamState)))
                    entity.Brain.AddState(new RoamState());
                if (!entity.Brain.states.ContainsKey(GetStateId(AICustomState.ThrowGrenadeState)))
                    entity.Brain.AddState(new ThrowGrenadeState());
                entity.Brain.InstanceSpecificDesign = _customDesign;
            }
            else
            {
                entity.Brain.InstanceSpecificDesign = null;
            }
            entity.Brain.LoadAIDesignAtIndex(entity.Brain.LoadedDesignIndex());
        }

        #endregion

        #region Custom States
        public class RoamState : BaseAIBrain<ScarecrowNPC>.BasicAIState
        {
            private StateStatus status = StateStatus.Error;
            public RoamState() : base(GetStateId(AICustomState.RoamState))
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

        public class ThrowGrenadeState : BaseAIBrain<ScarecrowNPC>.BasicAIState
        {
            bool _isThrown;
            NPCPlayer _entity = null;
            BasePlayer _target = null;
            Item _grenade = null;
            const float _MAX_DISTANCE = 5f;
            const float _THROW_TIME = 1.5f;

            public ThrowGrenadeState() : base(GetStateId(AICustomState.ThrowGrenadeState))
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
        #endregion
    }
}
