﻿// Visual Pinball Engine
// Copyright (C) 2021 freezy and VPE Team
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VisualPinball.Engine.Math;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;

namespace VisualPinball.Unity.Editor
{
	public delegate void OnDragPointPositionChange(Vector3 newPosition);

	public class DragPointsHandler
	{
		/// <summary>
		/// Component
		/// </summary>
		public IMainRenderableComponent MainComponent { get; }

		/// <summary>
		/// Component item as IDragPointsEditable
		/// </summary>
		public IDragPointsInspector DragPointInspector { get; }

		/// <summary>
		/// Transform component of the game object
		/// </summary>
		public Transform Transform { get; }

		/// <summary>
		/// Control points storing & rendering
		/// </summary>
		public List<ControlPoint> ControlPoints { get; } = new List<ControlPoint>();

		/// <summary>
		/// Scene view handler
		/// </summary>
		///
		/// <remarks>
		/// Will handle all the rendering part and update some handler's variables about curve traveller
		/// </remarks>
		private readonly DragPointsSceneViewHandler _sceneViewHandler;

		/// <summary>
		/// Drag points selection
		/// </summary>
		public List<ControlPoint> SelectedControlPoints { get; } = new List<ControlPoint>();
		private Vector3 _positionHandlePosition = Vector3.zero;

		/// <summary>
		/// Curve traveller handling
		/// </summary>
		///
		/// <remarks>
		/// CurveTravellerPosition, CurveTravellerControlPointIdx & CurveTravellerVisible will be updated by the DragPointsSceneViewHandler
		/// </remarks>
		public int CurveTravellerControlId { get; private set; }
		public Vector3 CurveTravellerPosition { get; set; } = Vector3.zero;
		public bool CurveTravellerVisible { get; set; }
		public int CurveTravellerControlPointIdx { get; set; } = -1;

		/// <summary>
		/// DragPoints flipping
		/// </summary>
		private Vector3 _flipAxis = Vector3.zero;

		/// <summary>
		/// Every DragPointsInspector instantiates this to manage its curve handling.
		/// </summary>
		/// <param name="mainComponent">The renderable main component, to retrieve IsLocked.</param>
		/// <param name="dragPointsInspector"></param>
		/// <exception cref="ArgumentException"></exception>
		public DragPointsHandler(IMainRenderableComponent mainComponent, IDragPointsInspector dragPointsInspector)
		{
			MainComponent = mainComponent;
			DragPointInspector = dragPointsInspector;

			Transform = mainComponent.gameObject.transform;

			_sceneViewHandler = new DragPointsSceneViewHandler(this){
				CurveWidth = 10.0f,
				CurveColor = Color.blue,
				CurveSlingShotColor = Color.red,
				ControlPointsSizeRatio = 1.0f,
				CurveTravellerSizeRatio = 0.75f
			};
		}

		/// <summary>
		/// References drag point data to control points.
		/// </summary>
		/// <returns>True if control points were re-built, false otherwise.</returns>
		public bool RemapControlPoints()
		{
			// if count differs, rebuild
			if (ControlPoints.Count != DragPointInspector.DragPoints.Length) {
				RebuildControlPoints();
				return true;
			}

			for (var i = 0; i < DragPointInspector.DragPoints.Length; ++i) {
				ControlPoints[i].DragPoint = DragPointInspector.DragPoints[i];
			}

			return false;
		}

		public DragPointData GetDragPoint(int controlId) => GetControlPoint(controlId)?.DragPoint;
		private ControlPoint GetControlPoint(int controlId)
			=> ControlPoints.Find(cp => cp.ControlId == controlId);

		/// <summary>
		/// Adds a new control point to the scene view and its drag point data
		/// to the game object.
		/// </summary>
		public void AddDragPointOnTraveller()
		{
			if (CurveTravellerControlPointIdx < 0 || CurveTravellerControlPointIdx >= ControlPoints.Count) {
				return;
			}

			var dragPoint = new DragPointData(DragPointInspector.DragPoints[CurveTravellerControlPointIdx]) {
				IsLocked = false
			};

			var newIdx = CurveTravellerControlPointIdx + 1;
			float ratio = (float)newIdx / DragPointInspector.DragPoints.Length;
			var dragPointPosition = Transform.worldToLocalMatrix.MultiplyPoint(CurveTravellerPosition);
			dragPointPosition -= DragPointInspector.EditableOffset;
			dragPointPosition -= DragPointInspector.GetDragPointOffset(ratio);
			dragPoint.Center = dragPointPosition.ToVertex3D();
			var dragPoints = DragPointInspector.DragPoints.ToList();
			dragPoints.Insert(newIdx, dragPoint);
			DragPointInspector.DragPoints = dragPoints.ToArray();

			ControlPoints.Insert(newIdx,
			new ControlPoint(
					DragPointInspector.DragPoints[newIdx],
					GUIUtility.GetControlID(FocusType.Passive),
					newIdx,
					ratio
			));
			RebuildControlPoints();
		}

		/// <summary>
		/// Removes a control point and its data.
		/// </summary>
		/// <param name="controlId"></param>
		public void RemoveDragPoint(int controlId)
		{
			var idx = ControlPoints.FindIndex(controlPoint => controlPoint.ControlId == controlId);
			if (idx < 0) {
				return;
			}
			var removalOk = !ControlPoints[idx].DragPoint.IsLocked;
			if (!removalOk) {
				removalOk = EditorUtility.DisplayDialog("Locked DragPoint Removal", "This drag point is locked!\nAre you really sure you want to remove it?", "Yes", "No");
			}
			if (!removalOk) {
				return;
			}
			var dragPoints = DragPointInspector.DragPoints.ToList();
			dragPoints.RemoveAt(idx);
			DragPointInspector.DragPoints = dragPoints.ToArray();

			RebuildControlPoints();
		}

		/// <summary>
		/// Flips all drag points around the given axis.
		/// </summary>
		/// <param name="flipAxis">Axis to flip</param>
		public void FlipDragPoints(FlipAxis flipAxis)
		{
			var axis = flipAxis == FlipAxis.X
				? _flipAxis.x
				: flipAxis == FlipAxis.Y ? _flipAxis.y : _flipAxis.z;

			var offset = DragPointInspector.EditableOffset;
			var wlMat = Transform.worldToLocalMatrix;

			foreach (var controlPoint in ControlPoints) {
				var coord = flipAxis == FlipAxis.X
					? controlPoint.WorldPos.x
					: flipAxis == FlipAxis.Y
						? controlPoint.WorldPos.y
						: controlPoint.WorldPos.z;

				coord = axis + (axis - coord);
				switch (flipAxis) {
					case FlipAxis.X:
						controlPoint.WorldPos.x = coord;
						break;
					case FlipAxis.Y:
						controlPoint.WorldPos.y = coord;
						break;
					case FlipAxis.Z:
						controlPoint.WorldPos.z = coord;
						break;
					default:
						throw new ArgumentOutOfRangeException(nameof(flipAxis), flipAxis, null);
				}
				controlPoint.UpdateDragPoint(DragPointInspector, Transform);
			}
		}

		/// <summary>
		/// Updates the lock status on all drag points to the given value.
		/// </summary>
		/// <param name="itemLock">New lock status</param>
		/// <returns>True if at least one lock status changed, false otherwise.</returns>
		public bool UpdateDragPointsLock(bool itemLock)
		{
			var lockChanged = false;
			foreach (var controlPoint in ControlPoints) {
				if (controlPoint.DragPoint.IsLocked != itemLock) {
					controlPoint.DragPoint.IsLocked = itemLock;
					lockChanged = true;
				}
			}
			return lockChanged;
		}

		/// <summary>
		/// Re-creates the control points of the scene view and references their
		/// drag point data.
		/// </summary>
		private void RebuildControlPoints()
		{
			ControlPoints.Clear();
			var dragPoints = DragPointInspector.DragPoints;
			for (var i = 0; i < dragPoints.Length; ++i) {
				var cp = new ControlPoint(
					dragPoints[i],
					GUIUtility.GetControlID(FocusType.Passive),
					i,
					dragPoints.Length > 1
						? (float)i / (dragPoints.Length - 1)
						: 0.0f
				);
				ControlPoints.Add(cp);
			}
			CurveTravellerControlId = GUIUtility.GetControlID(FocusType.Passive);

			// persist prefab changes
			EditorUtility.SetDirty(MainComponent.gameObject);
			PrefabUtility.RecordPrefabInstancePropertyModifications(MainComponent as Object);
		}

		/// <summary>
		/// Un-selects all control points.
		/// </summary>
		private void ClearAllSelection()
		{
			foreach (var controlPoint in ControlPoints) {
				controlPoint.IsSelected = false;
			}
		}

		/// <summary>
		/// Takes care of the rendering-related stuff.
		/// </summary>
		///
		/// <remarks>
		/// This is called by the drag point inspector.
		/// </remarks>
		///
		/// <param name="evt">Event from the inspector</param>
		/// <param name="onChange"></param>
		public void OnSceneGUI(Event evt, OnDragPointPositionChange onChange = null)
		{
			switch (evt.type) {

				case EventType.Layout:
					InitSceneGui();
					break;

				case EventType.MouseDown:
					OnMouseDown();
					break;

				case EventType.Repaint:
					CurveTravellerVisible = false;
					break;
			}

			// Handle the common position handler for all selected control points
			if (SelectedControlPoints.Count > 0) {
				var parentRot = Quaternion.identity;
				if (Transform.parent != null) {
					parentRot = Transform.parent.transform.rotation;
				}
				EditorGUI.BeginChangeCheck();
				var newHandlePos = HandlesUtils.HandlePosition(_positionHandlePosition, DragPointInspector.HandleType, parentRot);
				if (EditorGUI.EndChangeCheck()) {
					onChange?.Invoke(newHandlePos);
					var deltaPosition = newHandlePos - _positionHandlePosition;
					foreach (var controlPoint in SelectedControlPoints) {
						controlPoint.WorldPos += deltaPosition;
						controlPoint.UpdateDragPoint(DragPointInspector, Transform);
					}
				}
			}

			//Render the curve & drag points
			_sceneViewHandler.OnSceneGUI();
		}

		private void InitSceneGui()
		{
			SelectedControlPoints.Clear();
			_flipAxis = Vector3.zero;

			//Setup Screen positions & controlID for control points (in case of modification of drag points coordinates outside)
			foreach (var controlPoint in ControlPoints) {
				controlPoint.WorldPos = controlPoint.DragPoint.Center.ToUnityVector3();
				controlPoint.WorldPos += DragPointInspector.EditableOffset;
				controlPoint.WorldPos += DragPointInspector.GetDragPointOffset(controlPoint.IndexRatio);
				controlPoint.WorldPos = Transform.localToWorldMatrix.MultiplyPoint(controlPoint.WorldPos);
				_flipAxis += controlPoint.WorldPos;
				controlPoint.LocalPos = Handles.matrix.MultiplyPoint(controlPoint.WorldPos);
				if (controlPoint.IsSelected) {
					if (!controlPoint.DragPoint.IsLocked) {
						SelectedControlPoints.Add(controlPoint);
					}
				}

				HandleUtility.AddControl(controlPoint.ControlId,
					HandleUtility.DistanceToCircle(controlPoint.LocalPos,
						HandleUtility.GetHandleSize(controlPoint.WorldPos) * ControlPoint.ScreenRadius *
						_sceneViewHandler.ControlPointsSizeRatio));
			}

			if (ControlPoints.Count > 0) {
				_flipAxis /= ControlPoints.Count;
			}

			//Setup PositionHandle if some control points are selected
			if (SelectedControlPoints.Count > 0) {
				_positionHandlePosition = Vector3.zero;
				foreach (var sCp in SelectedControlPoints) {
					_positionHandlePosition += sCp.WorldPos;
				}

				_positionHandlePosition /= SelectedControlPoints.Count;
			}

			if (CurveTravellerVisible) {
				HandleUtility.AddControl(CurveTravellerControlId,
					HandleUtility.DistanceToCircle(Handles.matrix.MultiplyPoint(CurveTravellerPosition),
						HandleUtility.GetHandleSize(CurveTravellerPosition) * ControlPoint.ScreenRadius *
						_sceneViewHandler.CurveTravellerSizeRatio * 0.5f));
			}
		}

		private void OnMouseDown()
		{
			if (Event.current.button == 0) {
				var nearestControlPoint = ControlPoints.Find(cp => cp.ControlId == HandleUtility.nearestControl);
				if (nearestControlPoint != null && !nearestControlPoint.DragPoint.IsLocked) {
					if (!Event.current.control) {
						ClearAllSelection();
						nearestControlPoint.IsSelected = true;
					}
					else {
						nearestControlPoint.IsSelected = !nearestControlPoint.IsSelected;
					}

					Event.current.Use();
				}
			}
		}
	}
}
