﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using RWCustom;

namespace RegionKit.Modules.Objects
{
	//todo: apply
	//Made By LeeMoriya
	public class Enums_ARKillRect
	{
		public static PlacedObject.Type ARKillRect = new(nameof(ARKillRect), true);
	}

	public class ARKillRect : UpdatableAndDeletable
	{
		public PlacedObject _PO;
		public IntRect _rect;

		public ARKillRect(Room room, PlacedObject pObj)
		{
			this.room = room;
			_PO = pObj;
			_rect = (_PO.data as PlacedObject.GridRectObjectData)!.Rect;
		}

		public override void Update(bool eu)
		{
			base.Update(eu);
			for (int i = 0; i < room.physicalObjects.Length; i++)
			{
				for (int j = 0; j < room.physicalObjects[i].Count; j++)
				{
					for (int k = 0; k < room.physicalObjects[i][j].bodyChunks.Length; k++)
					{
						if (Custom.InsideRect(room.GetTilePosition(room.physicalObjects[i][j].bodyChunks[k].pos), _rect))
						{
							if (room.physicalObjects[i][j] is Creature crit)
							{
								if (!crit.dead)
								{
									crit.Die();
								}
							}
						}
					}
				}
			}
		}
	}
}
