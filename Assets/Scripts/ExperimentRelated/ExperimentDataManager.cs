﻿using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Valve.VR;

public enum ExperimentType { none = -1, detection, effectiveness }
public enum RedirectionAlgorithms { S2C = 0, AC2F }
public enum RecordedGainTypes { none = -1, rotationAgainstHead, rotationWithHead, curvature }
public enum DistractorType { none = 0, contrabass, oboe, harpsichord, violin, glockenspiel }

#region Serialized Data Classes
[System.Serializable]
public class IngameScoreData
{
    public int _id;
    public int _timeScore;
    public int _damageScore;
    public int _quizScore;
    public int _totalScore;
}

// Recorded per frame
[System.Serializable]
public class RedirectionFrameData
{
    public int _id;
    public bool _gainDetected;

    public Vector3 _deltaPos;
    public float _deltaDir;
    public float _deltaTime;

    public bool _inReset;

    public RedirectionAlgorithms _currentActiveAlgorithm;
    public DistractorType _currentActiveDistractor;

    public RecordedGainTypes _currentlyAppliedGain;
    public float _currentRotationGainAgainst;
    public float _currentRotationGainWith;
    public float _currentCurvatureGain;

    public float _noGainRatioAtDetection = 0;
    public float _negativeRotationGainRatioAtDetection = 0;
    public float _positiveRotationGainRatioAtDetection = 0;
    public float _curvatureGainRatioAtDetection = 0;
}
#endregion

/// <summary>
/// Management class for data collection.
/// </summary>
public class ExperimentDataManager : MonoBehaviour
{
    public ExperimentType _experimentType;
    public string _gameScoreFileName = "GameScoresEx1.dat";
    public string _detectionDataFileName = "DetectionDataEx1.dat";

    [Header("Experiment 1 Specific")]
    public float _gainRatioSampleWindowInSeconds = 0.5f;
    public int _samplesPerSecond = 90;

    [HideInInspector]
    public int _currentParticipantId = 0;
    [HideInInspector]
    public List<int> _previousGameScores = new List<int>();
    [HideInInspector]
    public bool _recordingActive = false;
    [HideInInspector]
    public CircularBuffer.CircularBuffer<RecordedGainTypes> _appliedGainsTimeSample;

    private GameManager _gameManager;
    private List<RedirectionFrameData> _detectionFrameData = new List<RedirectionFrameData>();
    private PlayerManager _playerManager;
    private RedirectionManagerER _redirectionManager;

    private float _sampleTimer = 0f;

    private GainIncrementer _gainIncrementer;

    private void Start()
    {
        _gameManager = GameObject.Find("GameManager").GetComponent<GameManager>();
        _playerManager = _gameManager.GetCurrentPlayerManager();
        _redirectionManager = _gameManager._redirectionManager;
        AcquireNewID();

        _appliedGainsTimeSample = new CircularBuffer.CircularBuffer<RecordedGainTypes>((int)(_samplesPerSecond * _gainRatioSampleWindowInSeconds));

        _gainIncrementer = GetComponent<GainIncrementer>();
        if(_experimentType == ExperimentType.detection)
        {
            _gainIncrementer.enabled = true;
        }
        else
        {
            _gainIncrementer.enabled = false;
        }
    }

    private void Update()
    {
        if (!_recordingActive)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CancelExperiment();
        }


        var newData = new RedirectionFrameData();
        newData._id = _currentParticipantId;
#if UNITY_EDITOR
        newData._gainDetected = (SteamVR.active && SteamVR_Actions._default.MenuButton.GetStateDown(_playerManager._batonHand)) || Input.GetKeyDown(KeyCode.E) ? true : false;
#else
        newData._gainDetected = (SteamVR.active && SteamVR_Actions._default.MenuButton.GetStateDown(_playerManager._batonHand)) ? true : false;
#endif
        newData._deltaPos = _redirectionManager.deltaPos;
        newData._deltaDir = _redirectionManager.deltaDir;
        newData._deltaTime = Time.deltaTime;
        newData._inReset = _redirectionManager.inReset;
        newData._currentActiveAlgorithm = _redirectionManager._currentActiveRedirectionAlgorithmType;
        newData._currentActiveDistractor = _redirectionManager._currentActiveDistractor != null ? _redirectionManager._currentActiveDistractor._distractorType : DistractorType.none;
        newData._currentlyAppliedGain = _redirectionManager.redirector._currentlyAppliedGainType;
        newData._currentRotationGainAgainst = _redirectionManager.MIN_ROT_GAIN;
        newData._currentRotationGainWith = _redirectionManager.MAX_ROT_GAIN;
        newData._currentCurvatureGain = _redirectionManager.CURVATURE_RADIUS;

        _sampleTimer += Time.deltaTime;
        if (_sampleTimer >= _gainRatioSampleWindowInSeconds / _samplesPerSecond)
        {
            _sampleTimer -= _gainRatioSampleWindowInSeconds / _samplesPerSecond;
            _appliedGainsTimeSample.PushFront(newData._currentlyAppliedGain);
        }

        if (newData._gainDetected)
        {
            var noGainFrequency = 0;
            var negativeRotationGainFrequency = 0;
            var positiveRotationGainFrequency = 0;
            var curvatureGainFrequency = 0;
            foreach(var sampledGain in _appliedGainsTimeSample)
            {
                switch(sampledGain)
                {
                    case RecordedGainTypes.none: noGainFrequency++; break;
                    case RecordedGainTypes.rotationAgainstHead: negativeRotationGainFrequency++; break;
                    case RecordedGainTypes.rotationWithHead: positiveRotationGainFrequency++; break;
                    case RecordedGainTypes.curvature: curvatureGainFrequency++; break;
                }
            }

            newData._noGainRatioAtDetection = noGainFrequency / _appliedGainsTimeSample.Size;
            newData._negativeRotationGainRatioAtDetection = negativeRotationGainFrequency / _appliedGainsTimeSample.Size;
            newData._positiveRotationGainRatioAtDetection = positiveRotationGainFrequency / _appliedGainsTimeSample.Size;
            newData._curvatureGainRatioAtDetection = curvatureGainFrequency / _appliedGainsTimeSample.Size;
        }

        _detectionFrameData.Add(newData);
    }

    public void CancelExperiment()
    {
        _recordingActive = false;
        var incompleteData = new IngameScoreData();
        incompleteData._id = _currentParticipantId;
        incompleteData._quizScore = -1;
        incompleteData._damageScore = -1;
        incompleteData._timeScore = -1;
        incompleteData._totalScore = -1;

        WriteGamePerformanceToFile(incompleteData);
        WriteDetectionPerformanceToFile();
    }

    public void WriteGamePerformanceToFile(IngameScoreData data)
    {
        if (File.Exists(Application.dataPath + "/" + _gameScoreFileName))
        {
            // Append
            using (var appender = File.AppendText(Application.dataPath + "/" + _gameScoreFileName))
            {
                var column1 = _currentParticipantId.ToString();
                var column2 = data._timeScore.ToString();
                var column3 = data._damageScore.ToString();
                var column4 = data._quizScore.ToString();
                var column5 = data._totalScore.ToString();
                var line = string.Format("{0},{1},{2},{3},{4}", column1, column2, column3, column4, column5);
                appender.WriteLine(line);
                appender.Flush();
            }
        }
        else
        {
            // Write
            using (var writer = new StreamWriter(Application.dataPath + "/" + _gameScoreFileName))
            {
                var column1 = "ParticipantID";
                var column2 = "TimeScore";
                var column3 = "DamageScore";
                var column4 = "QuizScore";
                var column5 = "TotalScore";
                var line = string.Format("{0},{1},{2},{3},{4}", column1, column2, column3, column4, column5);
                writer.WriteLine(line);
                writer.Flush();

                column1 = _currentParticipantId.ToString();
                column2 = data._timeScore.ToString();
                column3 = data._damageScore.ToString();
                column4 = data._quizScore.ToString();
                column5 = data._totalScore.ToString();
                line = string.Format("{0},{1},{2},{3},{4}", column1, column2, column3, column4, column5);
                writer.WriteLine(line);
                writer.Flush();
            }
        }
    }

    public void WriteDetectionPerformanceToFile()
    {
        if (File.Exists(Application.dataPath + "/" + _detectionDataFileName))
        {
            // Append
            using (var appender = File.AppendText(Application.dataPath + "/" + _detectionDataFileName))
            {
                string column1,  column2,  column3,  column4,  column5,  column6,  column7,  column8,  column9,  column10, 
                       column11, column12, column13, column14, column15, column16, column17, column18, column19, column20, 
                       column21, column22, column23, column24, column25, line;
                foreach (var frame in _detectionFrameData)
                {
                    column1 = frame._id.ToString();
                    column2 = (frame._gainDetected ? 1 : 0).ToString();
                    column3 = frame._deltaPos.magnitude.ToString(CultureInfo.InvariantCulture);
                    column4 = frame._deltaDir.ToString(CultureInfo.InvariantCulture);
                    column5 = frame._deltaTime.ToString(CultureInfo.InvariantCulture);
                    column6 = (frame._inReset ? 1 : 0).ToString();

                    column7 = (frame._currentActiveAlgorithm == RedirectionAlgorithms.S2C ? 1 : 0).ToString();
                    column8 = (frame._currentActiveAlgorithm == RedirectionAlgorithms.AC2F ? 1 : 0).ToString();

                    column9 = (frame._currentActiveDistractor == DistractorType.none ? 1 : 0).ToString();
                    column10 = (frame._currentActiveDistractor == DistractorType.contrabass ? 1 : 0).ToString();
                    column11 = (frame._currentActiveDistractor == DistractorType.oboe ? 1 : 0).ToString();
                    column12 = (frame._currentActiveDistractor == DistractorType.harpsichord ? 1 : 0).ToString();
                    column13 = (frame._currentActiveDistractor == DistractorType.violin ? 1 : 0).ToString();
                    column14 = (frame._currentActiveDistractor == DistractorType.glockenspiel ? 1 : 0).ToString();

                    column15 = (frame._currentlyAppliedGain == RecordedGainTypes.none ? 1 : 0).ToString();
                    column16 = (frame._currentlyAppliedGain == RecordedGainTypes.rotationAgainstHead ? 1 : 0).ToString();
                    column17 = (frame._currentlyAppliedGain == RecordedGainTypes.rotationWithHead ? 1 : 0).ToString();
                    column18 = (frame._currentlyAppliedGain == RecordedGainTypes.curvature ? 1 : 0).ToString();

                    column19 = frame._currentRotationGainAgainst.ToString(CultureInfo.InvariantCulture);
                    column20 = frame._currentRotationGainWith.ToString(CultureInfo.InvariantCulture);
                    column21 = frame._currentCurvatureGain.ToString(CultureInfo.InvariantCulture);

                    column22 = frame._noGainRatioAtDetection.ToString("F3");
                    column23 = frame._negativeRotationGainRatioAtDetection.ToString("F3");
                    column24 = frame._positiveRotationGainRatioAtDetection.ToString("F3");
                    column25 = frame._curvatureGainRatioAtDetection.ToString("F3");

                    line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24}", column1, column2, column3, column4, column5, column6, column7, column8, column9, column10, column11, column12, column13, column14, column15, column16, column17, column18, column19, column20, column21, column22, column23, column24, column25);
                    appender.WriteLine(line);
                    appender.Flush();
                }
            }
        }
        else
        {
            // Write
            using (var writer = new StreamWriter(Application.dataPath + "/" + _detectionDataFileName))
            {
                var column1 = "ParticipantID";
                var column2 = "GainDetected";
                var column3 = "DeltaPosMagnitude";
                var column4 = "DeltaDir";
                var column5 = "DeltaTime";
                var column6 = "ResetActive";

                var column7 = "S2CActive";
                var column8 = "AC2FActive";

                var column9 = "NoDistractorActive";
                var column10 = "ContrabassDistractorActive";
                var column11 = "OboeDistractorActive";
                var column12 = "HarpsichordDistractorActive";
                var column13 = "ViolinDistractorActive";
                var column14 = "GlockenspielDistractorActive";

                var column15 = "NoGainApplied";
                var column16 = "NegativeRotationGainApplied";
                var column17 = "PositiveRotationGainApplied";
                var column18 = "CurvatureGainApplied";

                var column19 = "CurrentRotationGainAgainst";
                var column20 = "CurrentRotationGainWith";
                var column21 = "CurrentCurvatureGainRadius";

                var column22 = "NoGainRatioDuringDetection";
                var column23 = "NegativeRotationGainRatioDuringDetection";
                var column24 = "PositiveRotationGainRatioDuringDetection";
                var column25 = "CurvatureGainRatioDuringDetection";

                var line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24}", column1, column2, column3, column4, column5, column6, column7, column8, column9, column10, column11, column12, column13, column14, column15, column16, column17, column18, column19, column20, column21, column22, column23, column24, column25);
                writer.WriteLine(line);
                writer.Flush();

                foreach (var frame in _detectionFrameData)
                {
                    column1 = frame._id.ToString();
                    column2 = (frame._gainDetected ? 1 : 0).ToString();
                    column3 = frame._deltaPos.magnitude.ToString(CultureInfo.InvariantCulture);
                    column4 = frame._deltaDir.ToString(CultureInfo.InvariantCulture);
                    column5 = frame._deltaTime.ToString(CultureInfo.InvariantCulture);
                    column6 = (frame._inReset ? 1 : 0).ToString();

                    column7 = (frame._currentActiveAlgorithm == RedirectionAlgorithms.S2C ? 1 : 0).ToString();
                    column8 = (frame._currentActiveAlgorithm == RedirectionAlgorithms.AC2F ? 1 : 0).ToString();

                    column9 = (frame._currentActiveDistractor == DistractorType.none ? 1 : 0).ToString();
                    column10 = (frame._currentActiveDistractor == DistractorType.contrabass ? 1 : 0).ToString();
                    column11 = (frame._currentActiveDistractor == DistractorType.oboe ? 1 : 0).ToString();
                    column12 = (frame._currentActiveDistractor == DistractorType.harpsichord ? 1 : 0).ToString();
                    column13 = (frame._currentActiveDistractor == DistractorType.violin ? 1 : 0).ToString();
                    column14 = (frame._currentActiveDistractor == DistractorType.glockenspiel ? 1 : 0).ToString();

                    column15 = (frame._currentlyAppliedGain == RecordedGainTypes.none ? 1 : 0).ToString();
                    column16 = (frame._currentlyAppliedGain == RecordedGainTypes.rotationAgainstHead ? 1 : 0).ToString();
                    column17 = (frame._currentlyAppliedGain == RecordedGainTypes.rotationWithHead ? 1 : 0).ToString();
                    column18 = (frame._currentlyAppliedGain == RecordedGainTypes.curvature ? 1 : 0).ToString();

                    column19 = frame._currentRotationGainAgainst.ToString(CultureInfo.InvariantCulture);
                    column20 = frame._currentRotationGainWith.ToString(CultureInfo.InvariantCulture);
                    column21 = frame._currentCurvatureGain.ToString(CultureInfo.InvariantCulture);

                    column22 = frame._noGainRatioAtDetection.ToString("F3");
                    column23 = frame._negativeRotationGainRatioAtDetection.ToString("F3");
                    column24 = frame._positiveRotationGainRatioAtDetection.ToString("F3");
                    column25 = frame._curvatureGainRatioAtDetection.ToString("F3");

                    line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24}", column1, column2, column3, column4, column5, column6, column7, column8, column9, column10, column11, column12, column13, column14, column15, column16, column17, column18, column19, column20, column21, column22, column23, column24, column25);
                    writer.WriteLine(line);
                    writer.Flush();
                }
            }
        }
    }

    private void AcquireNewID()
    {
        if (!File.Exists(Application.dataPath + "/" + _gameScoreFileName))
        {
            _currentParticipantId = 0;
            return;
        }

        using (var reader = new StreamReader(Application.dataPath + "/" + _gameScoreFileName))
        {
            var list1 = new List<string>();
            var list2 = new List<string>();
            var list3 = new List<string>();
            var list4 = new List<string>();
            var list5 = new List<string>();
            reader.ReadLine();

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                var values = line.Split(',');

                list1.Add(values[0]);
                list2.Add(values[1]);
                list3.Add(values[2]);
                list4.Add(values[3]);
                list5.Add(values[4]);
            }

            _currentParticipantId = int.Parse(list1[list1.Count - 1]) + 1;
            _previousGameScores = list5.Select(int.Parse).ToList();
        }
    }
}
