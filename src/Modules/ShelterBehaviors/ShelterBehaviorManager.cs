﻿using System.Collections.Generic;
using System.Linq;
using RWCustom;
using UnityEngine;
namespace RegionKit.Modules.ShelterBehaviors
{
	/// <summary>
	/// As the name suggests...
	/// </summary>
	public static class ExtensionsForThingsIHateTypingOut
	{
		public static float Abs(this float f)
		{
			return Mathf.Abs(f);
		}
		public static float Abs(this int f)
		{
			return Mathf.Abs(f);
		}
		public static float Sign(this float f)
		{
			return Mathf.Sign(f);
		}

		public static bool Contains(this IntRect rect, IntVector2 pos, bool incl = true) // Cmon joar
		{
			if (incl) return pos.x >= rect.left && pos.x <= rect.right && pos.y >= rect.bottom && pos.y <= rect.top;
			return pos.x > rect.left && pos.x < rect.right && pos.y > rect.bottom && pos.y < rect.top;
		}

		public static Vector2 ToCardinals(this Vector2 dir)
		{
			return new Vector2(Vector2.Dot(Vector2.right, dir).Abs() > 0.707 ? Vector2.Dot(Vector2.right, dir).Sign() : 0, Vector2.Dot(Vector2.up, dir).Abs() > 0.707 ? Vector2.Dot(Vector2.up, dir).Sign() : 0f);
		}
	}

	/// <summary>
	/// Interface used to notify <see cref="UpdatableAndDeletable"/>s about shelter door related events. Notifications are issued by an instance of <see cref="ShelterBehaviorManager"/> in the room.
	/// </summary>
	public interface IReactToShelterEvents
	{
		/// <summary>
		/// Notification about shelter closing/opening state.
		/// </summary>
		/// <param name="newFactor">New value of close/open factor; similar to <see cref="ShelterDoor.closedFac"/>.</param>
		/// <param name="closeSpeed">Current speed of doors closing.</param>
		void ShelterEvent(float newFactor, float closeSpeed);
	}
	/// <summary>
	/// Main object used to change how shelters behave; required for all other placedObjects to function.
	/// </summary>
	public class ShelterBehaviorManager : UpdatableAndDeletable, INotifyWhenRoomIsReady
	{
		/// <summary>
		/// Whether vanilla door should be disabled.
		/// </summary>
		private bool _noVanillaDoors;
		/// <summary>
		/// Default spawn pos for the room.
		/// </summary>
		private IntVector2 _vanillaSpawnPosition;
		/// <summary>
		/// List of tiles to <see cref="AbstractCreature.RealizeInRoom"/> on; is linearly cycled through each time a new creature wants to realize.
		/// </summary>
		private List<IntVector2> _spawnPositions;
		/// <summary>
		/// Additional doors managed by the object.
		/// </summary>
		private List<ShelterDoor> _customDoors;
		/// <summary>
		/// Whether the shelter requires player to hold Down control to sleep.
		/// </summary>
		private bool holdToTrigger { get { return _htt || Override_HTT; } set { _htt = value; } }
		/// <summary>
		/// Global HTT override.
		/// </summary>
		public static bool Override_HTT = IO.File.Exists(AssetManager.ResolveFilePath("world/htt.txt"));
		private bool _htt;

		/// <summary>
		/// Trigger zones.
		/// </summary>
		private List<IntRect> _triggers;
		/// <summary>
		/// No-trigger zones.
		/// </summary>
		private List<IntRect> _noTriggers;
		/// <summary>
		/// Whether shelter is treated as broken.
		/// </summary>
		private bool _broken;
		/// <summary>
		/// Alternative to <see cref="Player.touchedNoInputCounter"/>.
		/// </summary>
		private int _noMovingCounter;
		/// <summary>
		/// Whether the shelter is currently closing.
		/// </summary>
		private bool _closing;
		/// <summary>
		/// Weakdict for replacement of <see cref="Player.forceSleepCounter"/>.
		/// </summary>
		private AttachedField<Player, int> _actualForceSleepCounter;
		private bool _debug = false;
		/// <summary>
		/// Close counter for situation where <see cref="_hasNoDoors"/> is true.
		/// </summary>
		private int _noDoorCloseCount;
		/// <summary>
		/// Whether there is no vanilla door or <see cref="_customDoors"/>.
		/// </summary>
		private bool _hasNoDoors;
		/// <summary>
		/// Short living door used to <see cref="CycleSpawnPosition"/>.
		/// </summary>
		private ShelterDoor? _tempSpawnPosHackDoor;
		/// <summary>
		/// Whether <see cref="_tempSpawnPosHackDoor"/> should be deleted next frame.
		/// </summary>
		private bool _deleteHackDoorNextFrame;
		/// <summary>
		/// Whether shelter marked as consumable is currently depleted.
		/// </summary>
		private bool _isConsumed;
		private int _placedObjectIndex;
		/// <summary>
		/// List of <see cref="IReactToShelterEvents"/> to notify when opening/closing.
		/// </summary>
		private List<IReactToShelterEvents> _subscribers;
		private readonly PlacedObject _pObj;
		private readonly ManagedData _data;
		private int _spawnCycleCtr;
		/// <summary>
		/// Unused thing for RNG salting in <see cref="GetSpawnPosition(int)"/>.
		/// </summary>
		static private int _incrementalsalt;
		private PlacedObject? _brokenWaterLevel;

		private void ContitionalLog(string str)
		{
			if (_debug && Input.GetKey("l"))
			{
				Debug.Log("Shelterbehaviormanager " + str);
			}
		}

		public ShelterBehaviorManager(Room instance, PlacedObject pObj)
		{
			this.room = instance;
			this._pObj = pObj;
			this._data = (pObj.data as ManagedData)!;
			this._placedObjectIndex = room.roomSettings.placedObjects.IndexOf(pObj);

			_spawnPositions = new List<IntVector2>();
			_customDoors = new List<ShelterDoor>();
			_triggers = new List<IntRect>();
			_noTriggers = new List<IntRect>();
			_subscribers = new List<IReactToShelterEvents>();

			_noVanillaDoors = false;
			this._broken = room.shelterDoor.Broken;
			this._vanillaSpawnPosition = room.shelterDoor.playerSpawnPos;

			_actualForceSleepCounter = new AttachedField<Player, int>();

			if (_data.GetValue<bool>("nvd")) this.RemoveVanillaDoors();
			if (_data.GetValue<bool>("htt")) holdToTrigger = true;

			if (room.game.session is StoryGameSession)
			{
				this._isConsumed = (room.game.session as StoryGameSession)!.saveState.ItemConsumed(room.world, false, this.room.abstractRoom.index, this._placedObjectIndex);
			}
			if (this._isConsumed)
			{
				this._broken = true;
				this.room.world.brokenShelters[this.room.abstractRoom.shelterIndex] = true;
			}

			this._brokenWaterLevel = null;
			for (int i = 0; i < instance.roomSettings.placedObjects.Count; i++)
			{
				if (instance.roomSettings.placedObjects[i].active)
				{
					switch (instance.roomSettings.placedObjects[i].type.ToString())
					{
					case _Module.EnumNames.PlacedDoor:
						this.AddPlacedDoor(instance.roomSettings.placedObjects[i]);
						break;
					case _Module.EnumNames.TriggerZone:
						this.AddTriggerZone(instance.roomSettings.placedObjects[i]);
						break;
					case _Module.EnumNames.NoTriggerZone:
						this.AddNoTriggerZone(instance.roomSettings.placedObjects[i]);
						break;
					case _Module.EnumNames.SpawnPosition:
						this.AddSpawnPosition(instance.roomSettings.placedObjects[i]);
						break;
					default:
						if (instance.roomSettings.placedObjects[i].type == PlacedObject.Type.BrokenShelterWaterLevel)
						{
							this._brokenWaterLevel = instance.roomSettings.placedObjects[i];
						}

						break;
					}
				}
			}
			_spawnCycleCtr = UnityEngine.Random.Range(0, _spawnPositions.Count);
		}

		/// <summary>
		/// Depletes the shelter.
		/// </summary>
		public void Consume()
		{
			if (!_data.GetValue<bool>("cs")) return;
			if (this._isConsumed) return;
			this._isConsumed = true;
			Debug.Log($"CONSUMED: Consumable Shelter in room {room.abstractRoom.name})");
			if (room.world.game.session is StoryGameSession)
			{
				int minCycles = _data.GetValue<int>("csmin");
				(room.world.game.session as StoryGameSession)!.saveState.ReportConsumedItem(room.world, false, this.room.abstractRoom.index, this._placedObjectIndex,
					(minCycles < 0) ? -1 : UnityEngine.Random.Range(minCycles, _data.GetValue<int>("csmax") + 1));
			}
		}

		/// <summary>
		/// Implemented from <see cref="INotifyWhenRoomIsReady"/>. 
		/// </summary>
		public void ShortcutsReady()
		{
			// housekeeping once all objects are placed
			this._hasNoDoors = _noVanillaDoors && (_customDoors.Count == 0);

			if (_hasNoDoors)
			{
				ApplySpawnHack(GetSpawnPosition(0));
			}

			float closedFac;
			float closeSpeed;
			if (this.room.game.world.rainCycle == null)
			{
				closedFac = 1f;
				closeSpeed = 1f;
			}
			else
			{
				closedFac = ((!room.game.setupValues.cycleStartUp) ? 0f : Mathf.InverseLerp(_data.GetValue<int>("ini") + _data.GetValue<int>("ouf"), _data.GetValue<int>("ini"), (float)this.room.game.world.rainCycle.timer));
				closeSpeed = this.room.game.world.rainCycle.timer <= _data.GetValue<int>("ini") ? 0f : -1f / (float)_data.GetValue<int>("ouf");
			}

			for (int i = 0; i < room.updateList.Count; i++)
			{
				if (room.updateList[i] is IReactToShelterEvents sub)
				{
					this._subscribers.Add(sub);
					(sub).ShelterEvent(closedFac, closeSpeed);
				}
			}
		}

		/// <summary>
		/// Implemented from <see cref="INotifyWhenRoomIsReady"/>
		/// </summary>
		public void AIMapReady()
		{
			_deleteHackDoorNextFrame = true;
		}


		public override void Update(bool eu)
		{
			if (_deleteHackDoorNextFrame)
			{
				if (_tempSpawnPosHackDoor != null)
				{
					_tempSpawnPosHackDoor.Destroy();
					room.updateList.Remove(_tempSpawnPosHackDoor);
					_tempSpawnPosHackDoor = null;
				}
				_deleteHackDoorNextFrame = false;
			}

			if (this.room.game.world.rainCycle.timer == _data.GetValue<int>("ini") && this.room.game.setupValues.cycleStartUp)
			{
				float closeSpeed = -1f / _data.GetValue<int>("ouf");
				foreach (var sub in _subscribers)
				{
					sub.ShelterEvent(1f, closeSpeed);
				}
			}

			base.Update(eu);
			ContitionalLog("Update");
			if (_noVanillaDoors)
			{
				ContitionalLog("Update no-vanilla-doors");
				// From Player update
				for (int i = 0; i < room.game.Players.Count; i++)
				{
					if (room.game.Players[i].realizedCreature != null && room.game.Players[i].realizedCreature.room == room)
					{
						ContitionalLog("Updating player " + i);
						Player p = (room.game.Players[i].realizedCreature as Player)!;
						if (p.room.abstractRoom.shelter && p.room.game.IsStorySession && !p.dead && !p.Sleeping && !_broken)// && p.room.shelterDoor != null && !p.room.shelterDoor.Broken)
						{
							if (!p.stillInStartShelter && p.FoodInRoom(p.room, false) >= ((!p.abstractCreature.world.game.GetStorySession.saveState.malnourished) ? p.slugcatStats.foodToHibernate : p.slugcatStats.maxFood))
							{
								p.readyForWin = true;
								p.forceSleepCounter = 0;
								ContitionalLog("ready a");
							}
							else if (p.room.world.rainCycle.timer > p.room.world.rainCycle.cycleLength)
							{
								p.readyForWin = true;
								p.forceSleepCounter = 0;
								ContitionalLog("ready b");
							}
							else if (p.input[0].y < 0 && !p.input[0].jmp && !p.input[0].thrw && !p.input[0].pckp && p.IsTileSolid(1, 0, -1) && !p.abstractCreature.world.game.GetStorySession.saveState.malnourished && p.FoodInRoom(p.room, false) > 0 && p.FoodInRoom(p.room, false) < p.slugcatStats.foodToHibernate && (p.input[0].x == 0 || ((!p.IsTileSolid(1, -1, -1) || !p.IsTileSolid(1, 1, -1)) && p.IsTileSolid(1, p.input[0].x, 0))))
							{
								p.forceSleepCounter++;
								ContitionalLog("force");
							}
							else
							{
								p.forceSleepCounter = 0;
								ContitionalLog("not ready");
							}
						}
					}
				}
				// From HUD update
				for (int i = 0; i < room.game.cameras.Length; i++)
				{
					if (room.game.cameras[i].room == room && room.game.cameras[i].hud != null)
					{
						ContitionalLog("Updated HUD");
						HUD.HUD hud = room.game.cameras[i].hud;
						if (hud.owner is Player p)
						{
							hud.showKarmaFoodRain = (p.RevealMap ||
								(p.room != null && p.room.abstractRoom.shelter && p.room.abstractRoom.realizedRoom != null && !this._broken));
							if (holdToTrigger && p.readyForWin) // trigger sleep
							{
								hud.foodMeter.forceSleep = 0;
								hud.foodMeter.showSurvLim = (float)hud.foodMeter.survivalLimit;
							}
						}
					}
				}
			} // end noVanillaDoors


			if (!_closing && !_broken)
			{
				ContitionalLog("Update not-closing");
				PreventVanillaClose();
				// handle player sleep and triggers
				for (int i = 0; i < room.game.Players.Count; i++)
				{
					if (room.game.Players[i].realizedCreature != null && room.game.Players[i].realizedCreature.room == room)
					{
						Player p = (room.game.Players[i].realizedCreature as Player)!;
						if (p.room.abstractRoom.shelter && p.room.game.IsStorySession && !p.dead)// && p.room.shelterDoor != null && !p.room.shelterDoor.Broken)
						{
							ContitionalLog("found player " + i);
							// if (holdToTrigger) p.readyForWin = false; // lets make a better use of this flag shall we
							if (!PlayersInTriggerZone())
							{
								ContitionalLog("player NOT in trigger zone");
								p.readyForWin = false;
								p.forceSleepCounter = 0;
								_actualForceSleepCounter[p] = 0;
								p.touchedNoInputCounter = Mathf.Min(p.touchedNoInputCounter, 19);
								p.sleepCounter = 0;
								this._noMovingCounter = _data.GetValue<int>("ftt");
							}
							else
							{
								ContitionalLog("player in trigger zone");
							}

							if (p.touchedNoInputCounter == 0)
							{
								_noMovingCounter = _data.GetValue<int>("ftt");
							}

							if (!holdToTrigger && p.readyForWin && p.touchedNoInputCounter > 1)
							{
								ContitionalLog("ready not moving");
								_noMovingCounter--;
								if (_noMovingCounter <= 0)
								{
									ContitionalLog("CLOSE due to ready");
									Close();
								}
							}
							else if (p.forceSleepCounter > 260 || _actualForceSleepCounter[p] > 260)
							{
								ContitionalLog("CLOSE due to force sleep");
								p.sleepCounter = -24;
								Close();
							}
							else if (p.readyForWin && holdToTrigger && p.input[0].y < 0 && !p.input[0].jmp && !p.input[0].thrw && !p.input[0].pckp && p.IsTileSolid(1, 0, -1) && (p.input[0].x == 0 || ((!p.IsTileSolid(1, -1, -1) || !p.IsTileSolid(1, 1, -1)) && p.IsTileSolid(1, p.input[0].x, 0))))
							{
								ContitionalLog("force sleep hold to trigger");
								// need something to preserve last counter through player update, zeroes if ready4win
								_actualForceSleepCounter[p] += _data.GetValue<int>("htts") - (_noVanillaDoors ? 0 : 1); // gets uses default for int so this works
								p.forceSleepCounter = _actualForceSleepCounter[p];
							}
							else if (_noVanillaDoors && p.input[0].y < 0 && !p.input[0].jmp && !p.input[0].thrw && !p.input[0].pckp && p.IsTileSolid(1, 0, -1) && (p.input[0].x == 0 || ((!p.IsTileSolid(1, -1, -1) || !p.IsTileSolid(1, 1, -1)) && p.IsTileSolid(1, p.input[0].x, 0))))
							{
								// allow starve
								_actualForceSleepCounter[p] += 1;
								p.forceSleepCounter = _actualForceSleepCounter[p];
							}
							else
							{
								_actualForceSleepCounter[p] = 0;
							}
						}
					}
				}
			}

			if (_closing && _hasNoDoors)
			{
				if (room.waterObject != null && _data.GetValue<bool>("ani") && _brokenWaterLevel != null)
				{
					room.waterObject.fWaterLevel = Mathf.Lerp(this.room.waterObject.originalWaterLevel, this._brokenWaterLevel.pos.y + 50f, Mathf.Pow((float)_noDoorCloseCount / ((float)_data.GetValue<int>("ftw") + 20f), 1.6f));
				}
				// Manage no-door logic
				if (_noDoorCloseCount == _data.GetValue<int>("fts"))
				{
					for (int j = 0; j < this.room.game.Players.Count; j++)
					{
						Player? p = (this.room.game.Players[j].realizedCreature as Player);
						if (p is null) continue;
						if (p.FoodInRoom(this.room, false) >= p.slugcatStats.foodToHibernate)
						{
							p.sleepWhenStill = true;
						}
					}
				}
				if (_noDoorCloseCount == _data.GetValue<int>("ftsv"))
				{
					for (int k = 0; k < this.room.game.Players.Count; k++)
					{
						Player? p = (this.room.game.Players[k].realizedCreature as Player);
						if (p is null) continue;
						if (this.room.game.Players[k].realizedCreature != null && p.FoodInRoom(this.room, false) < ((!this.room.game.GetStorySession.saveState.malnourished) ? 1 : p.slugcatStats.maxFood))
						{
							this.room.game.GoToStarveScreen();
						}
					}
				}
				if (_noDoorCloseCount == _data.GetValue<int>("ftw"))
				{
					bool flag = true;
					for (int i = 0; i < this.room.game.Players.Count; i++)
					{
						if (!this.room.game.Players[i].state.alive)
						{
							flag = false;
						}
					}
					if (flag)
					{
						Player? p = (this.room.game.Players[0].realizedCreature as Player);
						if (p is null) goto EXIT_;
						this.room.game.Win(p.FoodInRoom(this.room, false) < p.slugcatStats.foodToHibernate);
					}
					else
					{
						this.room.game.GoToDeathScreen();
					}
				}
			EXIT_:;
				_noDoorCloseCount++;
			}
		}

		/// <summary>
		/// Sends a close notification to all doors if neccessary.
		/// </summary>
		private void Close()
		{
			_closing = true;
			_noDoorCloseCount = 0;
			if (!_noVanillaDoors) room.shelterDoor.Close();
			foreach (var door in _customDoors)
			{
				door.Close();
			}
			float closeSpeed = 1f / (float)_data.GetValue<int>("ftw");
			foreach (var sub in _subscribers)
			{
				sub.ShelterEvent(0f, closeSpeed);
			}
			for (int i = 0; i < room.updateList.Count; i++)
			{
				if (room.updateList[i] is HoldToTriggerTutorialObject) (room.updateList[i] as HoldToTriggerTutorialObject)!.Consume();
			}
			Consume();
			if (_data.GetValue<bool>("ani") && _brokenWaterLevel != null)
			{
				room.AddWater(); // animate water level
				Debug.LogError("added watre");
			}
			Debug.Log("Shelterbehaviormanager CLOSE");
		}

		/// <summary>
		/// Makes the next creature to be realized spawn in a given position.
		/// </summary>
		/// <param name="coords">Tile to be treated as spawn point.</param>
		public void ApplySpawnHack(IntVector2 coords)
		{
			if (_tempSpawnPosHackDoor != null && room.updateList.Contains(_tempSpawnPosHackDoor)) room.updateList.Remove(_tempSpawnPosHackDoor);
			_tempSpawnPosHackDoor = new ShelterDoor(room);
			_tempSpawnPosHackDoor.closeTiles = new IntVector2[0];
			_tempSpawnPosHackDoor.playerSpawnPos = coords;
			room.updateList.Insert(0, _tempSpawnPosHackDoor);

		}
		/// <summary>
		/// Applies the next queued spawn position from <see cref="_spawnPositions"/>, applies vanilla one if there is none.
		/// </summary>
		public void CycleSpawnPosition()
		{
			_spawnCycleCtr++;
			if (_spawnCycleCtr >= _spawnPositions.Count) _spawnCycleCtr = 0;
			ApplySpawnHack((_spawnPositions.Count > 0) ? _spawnPositions[_spawnCycleCtr] : _vanillaSpawnPosition);
		}
		/// <summary>
		/// Checks whether players are in a zone eligible for starting sleep sequence.
		/// </summary>
		/// <returns></returns>
		private bool PlayersInTriggerZone()
		{
			for (int i = 0; i < room.game.Players.Count; i++) // Any alive players missing ? Still in starting shelter ?
			{
				AbstractCreature ap = room.game.Players[i];
				if (!ap.state.dead && ap.realizedCreature != null && ap.realizedCreature.room != room) return false;
				if ((ap.realizedCreature as Player)?.stillInStartShelter ?? false) return false;
			}
			if (_triggers.Count == 0 && _noTriggers.Count == 0) // No trigges, possibly vanilla behavior
			{
				if (_noVanillaDoors) return true;
				for (int i = 0; i < room.game.Players.Count; i++) // Any alive players missing ?
				{
					AbstractCreature ap = room.game.Players[i];
					if (Custom.ManhattanDistance(ap.pos.Tile, this.room.shortcuts[0].StartTile) <= 6) return false;
				}
				return true;
			}
			else
			{
				for (int i = 0; i < room.game.Players.Count; i++)
				{
					AbstractCreature ap = room.game.Players[i];
					foreach (var rect in _triggers)
					{
						if (!rect.Contains(ap.pos.Tile)) return false; // anyone out of a positive trigger
					}
					foreach (var rect in _noTriggers)
					{
						if (rect.Contains(ap.pos.Tile)) return false; // anyone in a negative trigger
					}
				}
			}
			return true;
		}
		/// <summary>
		/// Prevents vanilla door from closing if neccessary.
		/// </summary>
		private void PreventVanillaClose()
		{
			if (!_noVanillaDoors) room.shelterDoor.closeSpeed = Mathf.Min(0f, room.shelterDoor.closeSpeed);
		}
		/// <summary>
		/// Deletes vanilla door.
		/// </summary>
		internal void RemoveVanillaDoors()
		{
			room.shelterDoor.Destroy();
			room.CleanOutObjectNotInThisRoom(room.shelterDoor);
			room.shelterDoor = null;
			this._noVanillaDoors = true;
		}

		

		/// <summary>
		/// Chooses a random spawn pos from the list.
		/// </summary>
		/// <param name="salt"></param>
		/// <returns></returns>
		internal IntVector2 GetSpawnPosition(int salt)
		{
			int oldseed = RNG.seed;
			try
			{
				if (room.game.IsStorySession)
					RNG.seed = salt + _incrementalsalt++ + room.game.clock + room.game.GetStorySession.saveState.seed + room.game.GetStorySession.saveState.cycleNumber + room.game.GetStorySession.saveState.deathPersistentSaveData.deaths + room.game.GetStorySession.saveState.deathPersistentSaveData.survives + Mathf.FloorToInt(room.game.GetStorySession.difficulty * 100) + Mathf.FloorToInt(room.game.GetStorySession.saveState.deathPersistentSaveData.howWellIsPlayerDoing * 100);
				if (_noVanillaDoors)
				{
					if (_spawnPositions.Count > 0) return _spawnPositions[UnityEngine.Random.Range(0, _spawnPositions.Count)];
					return _vanillaSpawnPosition;
				}

				int roll = UnityEngine.Random.Range(0, _spawnPositions.Count + 1);
				if (_spawnPositions.Count < roll) return _spawnPositions[roll];
				return _vanillaSpawnPosition;
			}
			finally
			{
				RNG.seed = oldseed;
			}
		}
		/// <summary>
		/// Creates a door from a PlacedObject.
		/// </summary>
		/// <param name="placedObject">pObj to use; its <see cref="PlacedObject.data"/> must be an appropriate instance of <see cref="ManagedData"/>.</param> 
		public void AddPlacedDoor(PlacedObject placedObject)
		{
			int preCounter = room.game.rainWorld.progression.miscProgressionData.starvationTutorialCounter; // Prevent starvation tutorial dupes
			if (room.game.IsStorySession)
				room.game.rainWorld.progression.miscProgressionData.starvationTutorialCounter = 0;
			ShelterDoor newDoor = new ShelterDoor(room);
			if (room.game.IsStorySession)
				room.game.rainWorld.progression.miscProgressionData.starvationTutorialCounter = preCounter;

			//Vector2 origin = placedObject.pos;
			IntVector2 originTile = room.GetTilePosition(placedObject.pos);
			IntVector2 dir = (placedObject.data as ManagedData)!.GetValue<IntVector2>("dir");
			//dir = dir.ToCardinals();

			newDoor.pZero = room.MiddleOfTile(originTile);
			newDoor.dir = dir.ToVector2();
			for (int n = 0; n < 4; n++)
			{
				newDoor.closeTiles[n] = originTile + dir * (n + 2);
			}
			newDoor.pZero += newDoor.dir * 60f;
			newDoor.perp = Custom.PerpendicularVector(newDoor.dir);

			newDoor.playerSpawnPos = GetSpawnPosition(_customDoors.Count);

			_customDoors.Add(newDoor);
			room.AddObject(newDoor);
		}

		internal void AddSpawnPosition(PlacedObject placedObject)
		{
			this._spawnPositions.Add(room.GetTilePosition(placedObject.pos));
			// re-shuffle
			for (int i = 0; i < _customDoors.Count; i++)
			{
				_customDoors[i].playerSpawnPos = GetSpawnPosition(i);
			}
		}

		/// <summary>
		/// Registers a trigger zone.
		/// </summary>
		/// <param name="placedObject">pObj to use; its <see cref="PlacedObject.data"/> must be an appropriate instance of <see cref="PlacedObject.GridRectObjectData"/>.</param>
		public void AddTriggerZone(PlacedObject placedObject)
		{
			this._triggers.Add((placedObject.data as PlacedObject.GridRectObjectData)!.Rect);
		}
		/// <summary>
		/// Registers a no-trigger zone.
		/// </summary>
		/// <param name="placedObject">pObj to use; its <see cref="PlacedObject.data"/> must be an appropriate instance of <see cref="PlacedObject.GridRectObjectData"/>.</param>
		public void AddNoTriggerZone(PlacedObject placedObject)
		{
			this._noTriggers.Add((placedObject.data as PlacedObject.GridRectObjectData)!.Rect);
		}

		/// <summary>
		/// Displays a HTT tutorial message, then destroys itself.
		/// </summary>
		public class HoldToTriggerTutorialObject : UpdatableAndDeletable
		{
			public HoldToTriggerTutorialObject(Room room, PlacedObject pObj)
			{
				this.room = room;
				_placedObject = pObj;
				_placedObjectIndex = room.roomSettings.placedObjects.IndexOf(pObj);
				if (room.game.Players.Count == 0) this.Destroy();
				// player loaded in room
				foreach (var p in room.game.Players)
				{
					if (p.pos.room == room.abstractRoom.index)
					{
						this.Destroy();
					}
				}

				// recently displayed
				if (room.game.session is StoryGameSession)
				{
					if ((room.game.session as StoryGameSession)!.saveState.ItemConsumed(room.world, false, room.abstractRoom.index, _placedObjectIndex))
					{
						this.Destroy();
					}
				}

			}

			public override void Update(bool eu)
			{
				base.Update(eu);
				if (base.slatedForDeletetion) return;
				if (this.room.game.session.Players.Count < 1 || this.room.game.cameras.Length < 1) return;
				if (!room.BeingViewed) _message = 0;
				else if (this.room.game.session.Players[0].realizedCreature != null && this.room.game.cameras[0].hud != null && this.room.game.cameras[0].hud.textPrompt != null && this.room.game.cameras[0].hud.textPrompt.messages.Count < 1)
				{
					switch (this._message)
					{
					case 0:
						this.room.game.cameras[0].hud.textPrompt.AddMessage(this.room.game.manager.rainWorld.inGameTranslator.Translate("This place is safe from the rain and most predators"), 20, 160, true, true);
						this._message++;
						break;
					case 1:
						this.room.game.cameras[0].hud.textPrompt.AddMessage(this.room.game.manager.rainWorld.inGameTranslator.Translate("With enough food, hold DOWN to hibernate"), 40, 160, false, true);
						this._message++;
						break;
					default:
						this.Consume();
						break;
					}
				}
			}
			private int _message;
			private PlacedObject _placedObject;
			private int _placedObjectIndex;

			public void Consume()
			{
				Debug.Log("CONSUMED: HoldToTriggerTutorialObject ;)");
				if (room.world.game.session is StoryGameSession)
				{
					(room.world.game.session as StoryGameSession)!.saveState.ReportConsumedItem(room.world, false, room.abstractRoom.index, this._placedObjectIndex, (_placedObject.data as ManagedData)!.GetValue<int>("htttcd"));
				}
				this.Destroy();
			}
		}
	}
}
