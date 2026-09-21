using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using KmaxXR;
using UnityEngine.EventSystems;

/// <summary>
/// 扩展的拖拽类，支持禁用旋转。
/// </summary>
public class DragablePro : StylusDragable
{
    [SerializeField]
    private bool enableRotation = false;
    public override void StylusOnBeginDrag(PointerEventData eventData)
    {
        base.StylusOnBeginDrag(eventData);
        var pointer = KmaxPointer.PointerById(eventData.pointerId);
        if (pointer == null) return;
        var pose = pointer.EndpointPose;
        // Cache the initial grab state.
        this._initialGrabOffset =
            Quaternion.Inverse(this.transform.rotation) *
            (this.transform.position - pose.position);

        this._initialGrabRotation =
            Quaternion.Inverse(pose.rotation) *
            this.transform.rotation;
    }
    public override void StylusOnDrag(PointerEventData eventData)
    {
        var pointer = KmaxPointer.PointerById(eventData.pointerId);
        if (pointer == null) return;
        var pose = pointer.EndpointPose;

        if (enableRotation)
        {

            // Update the grab object's rotation.
            this.transform.rotation =
                pose.rotation * this._initialGrabRotation;

            // Update the grab object's position.
            this.transform.position =
                pose.position +
                (this.transform.rotation * this._initialGrabOffset);
        }
        else
        {
            // Update the grab object's position.
            this.transform.position =
                pose.position + (this.transform.rotation * this._initialGrabOffset);
        }
    }
    public override void StylusOnEndDrag(PointerEventData eventData)
    {
        base.StylusOnEndDrag(eventData);
    }
}
