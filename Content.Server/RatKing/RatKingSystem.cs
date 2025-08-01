// SPDX-FileCopyrightText: 2022 EmoGarbage404 <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Moony <moonheart08@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Vera Aguilera Puerto <6766154+Zumorica@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 metalgearsloth <comedian_vs_clown@hotmail.com>
// SPDX-FileCopyrightText: 2023 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Kara <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2023 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Centronias <me@centronias.com>
// SPDX-FileCopyrightText: 2024 Jake Huxell <JakeHuxell@pm.me>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 MilenVolf <63782763+MilenVolf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Tim <timfalken@hotmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Popups;
using Content.Shared.Atmos;
using Content.Shared.Chat; // Einstein Engines - Languages
using Content.Shared.Dataset;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Pointing;
using Content.Shared.Random.Helpers;
using Content.Shared.RatKing;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.RatKing
{
    /// <inheritdoc/>
    public sealed class RatKingSystem : SharedRatKingSystem
    {
        [Dependency] private readonly AtmosphereSystem _atmos = default!;
        [Dependency] private readonly ChatSystem _chat = default!;
        [Dependency] private readonly HTNSystem _htn = default!;
        [Dependency] private readonly HungerSystem _hunger = default!;
        [Dependency] private readonly NPCSystem _npc = default!;
        [Dependency] private readonly PopupSystem _popup = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<RatKingComponent, RatKingRaiseArmyActionEvent>(OnRaiseArmy);
            SubscribeLocalEvent<RatKingComponent, RatKingDomainActionEvent>(OnDomain);
            SubscribeLocalEvent<RatKingComponent, AfterPointedAtEvent>(OnPointedAt);
        }

        /// <summary>
        /// Summons an allied rat servant at the King, costing a small amount of hunger
        /// </summary>
        private void OnRaiseArmy(EntityUid uid, RatKingComponent component, RatKingRaiseArmyActionEvent args)
        {
            if (args.Handled)
                return;

            if (!TryComp<HungerComponent>(uid, out var hunger))
                return;

            //make sure the hunger doesn't go into the negatives
            if (_hunger.GetHunger(hunger) < component.HungerPerArmyUse)
            {
                _popup.PopupEntity(Loc.GetString("rat-king-too-hungry"), uid, uid);
                return;
            }
            args.Handled = true;
            _hunger.ModifyHunger(uid, -component.HungerPerArmyUse, hunger);
            var servant = Spawn(component.ArmyMobSpawnId, Transform(uid).Coordinates);
            var comp = EnsureComp<RatKingServantComponent>(servant);
            comp.King = uid;
            Dirty(servant, comp);

            component.Servants.Add(servant);
            _npc.SetBlackboard(servant, NPCBlackboard.FollowTarget, new EntityCoordinates(uid, Vector2.Zero));
            UpdateServantNpc(servant, component.CurrentOrder);
        }

        /// <summary>
        /// uses hunger to release a specific amount of ammonia into the air. This heals the rat king
        /// and his servants through a specific metabolism.
        /// </summary>
        private void OnDomain(EntityUid uid, RatKingComponent component, RatKingDomainActionEvent args)
        {
            if (args.Handled)
                return;

            if (!TryComp<HungerComponent>(uid, out var hunger))
                return;

            //make sure the hunger doesn't go into the negatives
            if (_hunger.GetHunger(hunger) < component.HungerPerDomainUse)
            {
                _popup.PopupEntity(Loc.GetString("rat-king-too-hungry"), uid, uid);
                return;
            }
            args.Handled = true;
            _hunger.ModifyHunger(uid, -component.HungerPerDomainUse, hunger);

            _popup.PopupEntity(Loc.GetString("rat-king-domain-popup"), uid);
            var tileMix = _atmos.GetTileMixture(uid, excite: true);
            tileMix?.AdjustMoles(Gas.Ammonia, component.MolesAmmoniaPerDomain);
        }

        private void OnPointedAt(EntityUid uid, RatKingComponent component, ref AfterPointedAtEvent args)
        {
            if (component.CurrentOrder != RatKingOrderType.CheeseEm)
                return;

            foreach (var servant in component.Servants)
            {
                _npc.SetBlackboard(servant, NPCBlackboard.CurrentOrderedTarget, args.Pointed);
            }
        }

        public override void UpdateServantNpc(EntityUid uid, RatKingOrderType orderType)
        {
            base.UpdateServantNpc(uid, orderType);

            if (!TryComp<HTNComponent>(uid, out var htn))
                return;

            if (htn.Plan != null)
                _htn.ShutdownPlan(htn);

            _npc.SetBlackboard(uid, NPCBlackboard.CurrentOrders, orderType);
            _htn.Replan(htn);
        }

        public override void DoCommandCallout(EntityUid uid, RatKingComponent component)
        {
            base.DoCommandCallout(uid, component);

            if (!component.OrderCallouts.TryGetValue(component.CurrentOrder, out var datasetId) ||
                !PrototypeManager.TryIndex<LocalizedDatasetPrototype>(datasetId, out var datasetPrototype))
                return;

            var msg = Random.Pick(datasetPrototype);
            _chat.TrySendInGameICMessage(uid, msg, InGameICChatType.Speak, true);
        }
    }
}
