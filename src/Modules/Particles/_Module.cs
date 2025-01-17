﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace RegionKit.Modules.Particles;

[RegionKitModule(nameof(Enable), nameof(Disable), moduleName: "Particles")]
internal static class _Module
{
	internal static bool __appliedOnce = false;
	internal static void Enable()
	{
		if (!__appliedOnce)
		{
			RegisterMPO();
		}
		__appliedOnce = true;
	}
	internal static void RegisterMPO()
	{
		//v1
		RegisterEmptyObjectType<V1.ParticleVisualCustomizer, ManagedRepresentation>("ParticleVisualCustomizer");
		RegisterEmptyObjectType<V1.ParticleBehaviourProvider.WavinessProvider, ManagedRepresentation>("ParticleWaviness");
		RegisterEmptyObjectType<V1.ParticleBehaviourProvider.SpinProvider, ManagedRepresentation>("ParticleSpin");
		RegisterEmptyObjectType<V1.ParticleBehaviourProvider.PlainModuleRegister, ManagedRepresentation>("GenericPBMDispenser");
		RegisterManagedObject<V1.RoomParticleSystem, V1.RectParticleSpawnerData, ManagedRepresentation>("RectParticleSpawner");
		RegisterManagedObject<V1.RoomParticleSystem, V1.OffscreenSpawnerData, ManagedRepresentation>("OffscreenParticleSpawner");
		RegisterManagedObject<V1.RoomParticleSystem, V1.WholeScreenSpawnerData, ManagedRepresentation>("WholeScreenSpawner");


	}
	internal static void Disable()
	{

	}
}
