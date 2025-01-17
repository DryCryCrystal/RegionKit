﻿using CoralBrain;
using UnityEngine;

namespace RegionKit.Modules.Objects
{
	public class RKProjectedCircle
	{
		public static class Enums_ProjectedCircle
		{
			public static PlacedObject.Type ProjectedCircle = new("ProjectedCircle", true);
		}

		public static void ApplyHooks()
		{
			_CommonHooks.PostRoomLoad += (self) =>
			{
				for (var i = 0; i < self.roomSettings.placedObjects.Count; i++)
				{
					var pObj = self.roomSettings.placedObjects[i];
					if (pObj.active && pObj.type == Enums_ProjectedCircle.ProjectedCircle)
						self.AddObject(new ProjectedCircleObject(self, pObj));
				}
			};
		}
	}

	public class ProjectedCircleObject : UpdatableAndDeletable, IOwnProjectedCircles
	{
		private readonly PlacedObject _pObj;

		public ProjectedCircleObject(Room room, PlacedObject pObj)
		{
			this.room = room;
			this._pObj = pObj;
			room.AddObject(new ProjectedCircle(room, this, 0, 180f));
		}

		public bool CanHostCircle() => true;

		public Vector2 CircleCenter(int index, float timeStacker) => _pObj.pos;

		public Room HostingCircleFromRoom() => this.room;
	}
}
