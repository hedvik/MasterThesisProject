﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using UnityEngine.XR;
using System.Linq;

public class GameManager : MonoBehaviour
{
    public GameObject _startGameTextBox;
    public AnimatedCharacterInterface _tutorialInstrument;
    public bool _skipTutorial;
    public float _levelUpDialogueBoxOffsetFromPlayer = 5f;
    public MountainKing _mountainKing;
    public GameObject _mountainKingEnsembleContainer;
    public AudioSource _mountainKingAudioSource;

    [SerializeField]
    private GameObject _levelUpDialoguePrefab = null;

    [HideInInspector]
    public bool _gameStarted = false;

    [HideInInspector]
    public LevelUpBox _levelUpDialogueBox;

    [HideInInspector]
    public UIManager _uiManager;
    [HideInInspector]
    public RedirectionManagerER _redirectionManager;

    private Queue<DialogueSnippet> _startGameDialogue;
    private Queue<DialogueSnippet> _tutorialDialogue;

    private bool _shieldEventTriggered = false;
    private bool _resetEventTriggered = false;
    private bool _attackEventTriggered = false;
    private AnimatedCharacterInterface _mountainKingAnimatedInterface;
    private AnimatedCharacterInterface[] _ensembleInstruments;

    private void Awake()
    {
        _uiManager = GameObject.Find("UIManager").GetComponent<UIManager>();
        _startGameDialogue = new Queue<DialogueSnippet>(Resources.LoadAll<DialogueSnippet>("ScriptableObjects/Dialogue/StartGame"));

        var tutorialDialogue = Resources.LoadAll<DialogueSnippet>("ScriptableObjects/Dialogue/Tutorial");
        _tutorialDialogue = new Queue<DialogueSnippet>(tutorialDialogue.OrderBy(c => c.name.Length).ThenBy(c => c.name));

        _uiManager.ActivateDialogue(this, typeof(GameManager).GetTypeInfo(), _startGameTextBox, _startGameDialogue);

        _redirectionManager = GameObject.Find(!XRSettings.enabled ? "Redirected Walker (Debug)" : "Redirected Walker (VR)").GetComponent<RedirectionManagerER>();
        _redirectionManager.MIN_ROT_GAIN = 0f;
        _redirectionManager.MAX_ROT_GAIN = 0f;

        // Setting curvature radius to 1000 is a rather hacky way of disabling it.
        // Curvature gains are technically disabled at infinite radius, 1000 is just an "approximation" of that.
        _redirectionManager.CURVATURE_RADIUS = 1000f;

        _levelUpDialogueBox = Instantiate(_levelUpDialoguePrefab).GetComponent<LevelUpBox>();
        _levelUpDialogueBox.gameObject.SetActive(false);
        _levelUpDialogueBox.transform.localScale = Vector3.zero;
        _mountainKing.InitialiseDistractor(_redirectionManager);
        _mountainKingAnimatedInterface = _mountainKing.GetComponent<AnimatedCharacterInterface>();
        _ensembleInstruments = _mountainKingEnsembleContainer.GetComponentsInChildren<AnimatedCharacterInterface>();
    }

    public void StartTutorial()
    {
        if (!_skipTutorial)
        {
            _tutorialInstrument.gameObject.SetActive(true);
            _uiManager.ActivateDialogue(_tutorialInstrument, typeof(AnimatedCharacterInterface).GetTypeInfo(), _tutorialInstrument.transform.Find("TextBox").gameObject, _tutorialDialogue);
            _tutorialInstrument.GetComponent<TutorialNPC>().InitialiseDistractor(_redirectionManager);
        }
        else
        {
            StartGame();
        }
    }

    public void StartGame()
    {
        _gameStarted = true;
        _redirectionManager.ActivateRotationAndCurvatureGains();

        if (!_skipTutorial)
        {
            _tutorialInstrument.GetComponent<TutorialNPC>().FinishTutorial();
        }
    }
    
    public void StartFinalBossAnimations()
    {
        _mountainKingAudioSource.Stop();
        _mountainKingAnimatedInterface.LookAtPosition(_redirectionManager.GetUserHeadTransform().position);
        foreach(var instrument in _ensembleInstruments)
        {
            instrument.AnimationTrigger("Idle");
            instrument.LookAtPosition(_redirectionManager.GetUserHeadTransform().position);
        }

        _mountainKingAnimatedInterface.AnimationTriggerWithCallback("Surprised", FinaliseFinalBossAnimations);
    }

    public void FinaliseFinalBossAnimations()
    {
        foreach (var instrument in _ensembleInstruments)
        {
            instrument.AnimationTrigger("Jumps");
        }
        _mountainKingAnimatedInterface.AnimationTriggerWithCallback("PhaseTransition", StartFinalBoss);
    }

    public void StartFinalBoss()
    {
        _mountainKingAudioSource.Play();
        _mountainKing.StartMountainKing();
    }

    public void EndGame()
    {
        foreach(var instrument in _ensembleInstruments)
        {
            instrument._eyeRenderer.material = _mountainKing._normalEyes;
        }
        // TODO: Display Score
        // TODO: Record data
    }

    public void ResetEventTriggerDialogue()
    {
        if (!_resetEventTriggered)
        {
            _uiManager.EventTriggerSnippet();
            _resetEventTriggered = true;
        }
    }

    public void ShieldEventTriggerDialogue()
    {
        if (!_shieldEventTriggered)
        {
            _uiManager.EventTriggerSnippet();
            _shieldEventTriggered = true;
        }
    }

    public void AttackEventTriggerDialogue()
    {
        if(!_attackEventTriggered)
        {
            _uiManager.EventTriggerSnippet();
            _attackEventTriggered = true;
        }
    }

    public PlayerManager GetCurrentPlayerManager()
    {
        return _redirectionManager.GetComponentInChildren<PlayerManager>();
    }
}
