#region Copyright & License Information
/*
 * Copyright 2007-2017 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using OpenRA.Effects;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	class NukePowerInfo : SupportPowerInfo, IRulesetLoaded, Requires<BodyOrientationInfo>
	{
		[WeaponReference, FieldLoader.Require]
		[Desc("Weapon to use for the impact.",
			"Also image to use for the missile.")]
		public readonly string MissileWeapon = "";

		[Desc("Delay (in ticks) after launch until the missile is spawned.")]
		public readonly int MissileDelay = 0;

		[Desc("Sprite sequence for the ascending missile.")]
		[SequenceReference("MissileWeapon")] public readonly string MissileUp = "up";

		[Desc("Sprite sequence for the descending missile.")]
		[SequenceReference("MissileWeapon")] public readonly string MissileDown = "down";

		[Desc("Offset from the actor the missile spawns on.")]
		public readonly WVec SpawnOffset = WVec.Zero;

		[Desc("Palette to use for the missile weapon image.")]
		[PaletteReference("IsPlayerPalette")] public readonly string MissilePalette = "effect";

		[Desc("Custom palette is a player palette BaseName.")]
		public readonly bool IsPlayerPalette = false;

		[Desc("Travel time - split equally between ascent and descent.")]
		public readonly int FlightDelay = 400;

		[Desc("Visual ascent velocity in WDist / tick.")]
		public readonly WDist FlightVelocity = new WDist(512);

		[Desc("Descend immediately on the target, with half the FlightDelay.")]
		public readonly bool SkipAscent = false;

		[Desc("Amount of time before detonation to remove the beacon.")]
		public readonly int BeaconRemoveAdvance = 25;

		[Desc("Range of cells the camera should reveal around target cell.")]
		public readonly WDist CameraRange = WDist.Zero;

		[Desc("Can the camera reveal shroud generated by the GeneratesShroud trait?")]
		public readonly bool RevealGeneratedShroud = true;

		[Desc("Reveal cells to players with these stances only.")]
		public readonly Stance CameraStances = Stance.Ally;

		[Desc("Amount of time before detonation to spawn the camera.")]
		public readonly int CameraSpawnAdvance = 25;

		[Desc("Amount of time after detonation to remove the camera.")]
		public readonly int CameraRemoveDelay = 25;

		[Desc("Corresponds to `Type` from `FlashPaletteEffect` on the world actor.")]
		public readonly string FlashType = null;

		public WeaponInfo WeaponInfo { get; private set; }

		public override object Create(ActorInitializer init) { return new NukePower(init.Self, this); }
		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			WeaponInfo weapon;
			var weaponToLower = (MissileWeapon ?? string.Empty).ToLowerInvariant();
			if (!rules.Weapons.TryGetValue(weaponToLower, out weapon))
				throw new YamlException("Weapons Ruleset does not contain an entry '{0}'".F(weaponToLower));

			WeaponInfo = weapon;

			base.RulesetLoaded(rules, ai);
		}
	}

	class NukePower : SupportPower
	{
		readonly NukePowerInfo info;
		readonly BodyOrientation body;

		public NukePower(Actor self, NukePowerInfo info)
			: base(self, info)
		{
			body = self.Trait<BodyOrientation>();
			this.info = info;
		}

		public override void Activate(Actor self, Order order, SupportPowerManager manager)
		{
			base.Activate(self, order, manager);
			PlayLaunchSounds();

			foreach (var launchpad in self.TraitsImplementing<INotifyNuke>())
				launchpad.Launching(self);

			var targetPosition = self.World.Map.CenterOfCell(order.TargetLocation);
			var palette = info.IsPlayerPalette ? info.MissilePalette + self.Owner.InternalName : info.MissilePalette;
			var missile = new NukeLaunch(self.Owner, info.MissileWeapon, info.WeaponInfo, palette, info.MissileUp, info.MissileDown,
				self.CenterPosition + body.LocalToWorld(info.SpawnOffset),
				targetPosition,
				info.FlightVelocity, info.FlightDelay, info.SkipAscent,
				info.FlashType);

			self.World.AddFrameEndTask(w => w.Add(new DelayedAction(info.MissileDelay, () => self.World.Add(missile))));

			if (info.CameraRange != WDist.Zero)
			{
				var type = info.RevealGeneratedShroud ? Shroud.SourceType.Visibility
					: Shroud.SourceType.PassiveVisibility;

				self.World.AddFrameEndTask(w => w.Add(new RevealShroudEffect(targetPosition, info.CameraRange, type, self.Owner, info.CameraStances,
					info.FlightDelay - info.CameraSpawnAdvance, info.CameraSpawnAdvance + info.CameraRemoveDelay)));
			}

			if (Info.DisplayBeacon)
			{
				var beacon = new Beacon(
					order.Player,
					targetPosition,
					Info.BeaconPaletteIsPlayerPalette,
					Info.BeaconPalette,
					Info.BeaconImage,
					Info.BeaconPoster,
					Info.BeaconPosterPalette,
					Info.ArrowSequence,
					Info.CircleSequence,
					Info.ClockSequence,
					() => missile.FractionComplete,
					Info.BeaconDelay,
					info.FlightDelay - info.BeaconRemoveAdvance);

				self.World.AddFrameEndTask(w =>
				{
					w.Add(beacon);
				});
			}
		}
	}
}