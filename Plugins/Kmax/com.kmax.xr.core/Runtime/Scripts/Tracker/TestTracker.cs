using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using System.Text;
using System;

namespace KmaxXR
{
    public class TestTracker : HeadTracker
    {
        private HandPose _handPose;
        private int frameId;
        private long delay, timestamp;

        public Pose pose => new Pose(_handPose.pos, _handPose.rot);

        public int FrameId => frameId;

        public long Delay => delay;
        public long Timestamp => timestamp;

        [SerializeField]
        Transform glass, pen;

        public override void StartTracking()
        {
            //base.StartTracking();
            KmaxNative.SetTracking(true, false);
        }

        public override void StopTracking()
        {
            //base.StopTracking();
            KmaxNative.SetTracking(false, false);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            PNClient.Start();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            PNClient.Stop();
        }

        protected override void UpdateTargetPose()
        {
            if (glass != null)
            {
                glass.gameObject.SetActive(eyePose.visible);
                glass.localPosition = eyePose.head;
                glass.localRotation = eyePose.look;
            }
            if (pen != null)
            {
                pen.gameObject.SetActive(_handPose.visible);
                pen.localPosition = _handPose.pos;
                pen.localRotation = _handPose.rot;
            }
        }

        internal override void ParseTrackerData(TrackerData data)
        {
            base.ParseTrackerData(data);
            _handPose.visible = data.penVisible > 0;
            _handPose.pos = data.pen.pos;
            _handPose.rot = data.pen.rot;
            frameId = data.frameId;
            // 获取当前时间戳
            timestamp = GetTimestampNow();
            //delay = timestamp - (long)data.timestamp;
            //Debug.Log($"Delta: {data.timestamp} ==> {timestamp} = {delay}");
        }

        public static long GetTimestampNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }
    }
}
