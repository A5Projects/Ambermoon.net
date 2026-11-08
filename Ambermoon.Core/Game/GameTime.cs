using System;
using Ambermoon.Data;
using Ambermoon.Data.Enumerations;

namespace Ambermoon;

partial class GameCore
{
    internal SavegameTime? GameTime { get; private set; } = null;
    bool preventTiredness;
    uint tirednessPreventionGeneration;

    public bool PreventTiredness
    {
        get => preventTiredness;
        set
        {
            preventTiredness = value;
            if (GameTime != null)
                GameTime.PreventTiredness = value;
            if (value)
            {
                ++tirednessPreventionGeneration;
                if (Ingame)
                    ClearPartyExhaustion();
            }
        }
    }

    void ClearPartyExhaustion()
    {
        for (int i = 0; i < MaxPartyMembers; ++i)
        {
            var member = GetPartyMember(i);
            if (member != null && member.Alive && member.Conditions.HasFlag(Condition.Exhausted))
            {
                RemoveCondition(Condition.Exhausted, member);
                layout.UpdateCharacter(member);
            }
        }
    }

    public void PauseGame()
    {
        if (gamePaused)
            return;

        gamePaused = true;
        audioWasEnabled = AudioOutput?.Enabled == true;
        musicWasPlaying = currentSong != null;
        gameWasPaused = paused;

        if (AudioOutput != null)
            AudioOutput.Enabled = false;

        Pause();
    }

    public void ResumeGame()
    {
        if (!gamePaused)
            return;

        gamePaused = false;

        if (!gameWasPaused)
            Resume();

        if (audioWasEnabled)
        {
            AudioOutput.Enabled = true;

            if (musicWasPlaying)
                ContinueMusic();
        }
    }

    public void Pause()
    {
        if (paused)
            return;

        paused = true;
        ResetMovementTimer();

        GameTime?.Pause();

        if (is3D)
            renderMap3D?.Pause();
        else
            renderMap2D?.Pause();

        Hook_Paused();
    }

    public void Resume()
    {
        if (!paused || WindowActive)
            return;

        paused = false;

        GameTime?.Resume();

        if (is3D)
            renderMap3D?.Resume();
        else
            renderMap2D?.Resume();

        Hook_Resumed();
    }

    internal void Wait(uint hours)
    {
        if (hours != 0)
            GameTime?.Wait(hours);
    }

    internal void EnableTimeEvents(bool enable) => disableTimeEvents = !enable;

    void GameTime_GotExhausted(uint hoursExhausted, uint hoursPassed)
    {
        if (disableTimeEvents)
            return;

        if (PreventTiredness)
        {
            GameTime_HoursPassed(hoursPassed, true);
            return;
        }

        swimDamageHandled = true;
        uint exhaustionGeneration = tirednessPreventionGeneration;
        bool alreadyExhausted = false;
        uint[] damageValues = new uint[MaxPartyMembers];

        for (int i = 0; i < MaxPartyMembers; ++i)
        {
            var partyMember = GetPartyMember(i);

            if (partyMember != null && partyMember.Alive)
            {
                bool exhausted = partyMember.Conditions.HasFlag(Condition.Exhausted);
                if (exhausted)
                    alreadyExhausted = true;
                partyMember.Conditions |= Condition.Exhausted;
                damageValues[i] = AddExhaustion(partyMember, hoursExhausted, !exhausted);
                if (damageValues[i] < partyMember.HitPoints.CurrentValue)
                    layout.UpdateCharacterStatus(partyMember);
            }
        }

        void DealDamage()
        {
            // A console command can enable the cheat while the warning popup
            // is open. Do not apply its already calculated exhaustion damage.
            if (PreventTiredness || exhaustionGeneration != tirednessPreventionGeneration)
            {
                // This warning already claimed the hourly swim-damage update.
                GameTime_HoursPassed(hoursPassed);
                return;
            }

            DamageAllPartyMembers(p => damageValues[SlotFromPartyMember(p)!.Value],
                null, null, someoneDied =>
                {
                    GameTime_HoursPassed(hoursPassed);

                    if (someoneDied)
                    {
                        CurrentMobileAction = MobileAction.None;
                        clickMoveActive = false;
                        ResetMoveKeys(true);
                    }
                });
        }

        if (!alreadyExhausted)
            ShowMessagePopup(DataNameProvider.ExhaustedMessage, DealDamage);
        else
            DealDamage();
    }

    void GameTime_GotTired(uint hoursPassed)
    {
        if (disableTimeEvents)
            return;

        if (PreventTiredness)
        {
            GameTime_HoursPassed(hoursPassed, true);
            return;
        }

        swimDamageHandled = true;
        ShowMessagePopup(DataNameProvider.TiredMessage, () => GameTime_HoursPassed(hoursPassed));
    }

    void GameTime_HoursPassed(uint hours, bool notTiredNorExhausted = false)
    {
        if (disableTimeEvents)
            return;

        ProcessPoisonDamage(hours, someoneDied =>
        {
            if (!notTiredNorExhausted && !swamLastTick && Map!.UseTravelTypes && TravelType == TravelType.Swim)
            {
                int hours = (int)(24 + GameTime!.Hour - lastSwimDamageHour) % 24;
                int minutes = (int)GameTime.Minute - (int)lastSwimDamageMinute;
                DoSwimDamage((uint)(hours * 12 + minutes / 5), someoneDrown =>
                {
                    if (someoneDied || someoneDrown)
                    {
                        CurrentMobileAction = MobileAction.None;
                        clickMoveActive = false;
                        ResetMoveKeys(true);
                    }
                });
            }
        });
    }

    void GameTime_NewDay(uint exhaustedHours, uint passedHours)
    {
        if (disableTimeEvents)
            return;

        ProcessDisease();

        void Age(PartyMember partyMember, Action finishAction)
            => AgePlayer(partyMember, finishAction, 1);

        ForeachPartyMember(Age, partyMember =>
            partyMember.Alive && partyMember.Conditions.HasFlag(Condition.Aging) &&
                !partyMember.Conditions.HasFlag(Condition.Petrified), () =>
                {
                    if (exhaustedHours > 0)
                        GameTime_GotExhausted(exhaustedHours, passedHours);
                    else if (CurrentSavegame!.HoursWithoutSleep >= 24)
                        GameTime_GotTired(passedHours);
                    else
                        GameTime_HoursPassed(passedHours, true);
                });
    }

    void GameTime_NewYear(uint exhaustedHours, uint passedHours)
    {
        if (disableTimeEvents)
            return;

        void Age(PartyMember partyMember, Action finishAction)
        {
            uint ageIncrease = partyMember.Conditions.HasFlag(Condition.Aging) ? 2u : 1u;
            AgePlayer(partyMember, finishAction, ageIncrease);
        }

        ForeachPartyMember(Age, partyMember =>
            partyMember.Alive && !partyMember.Conditions.HasFlag(Condition.Petrified), () =>
            {
                if (exhaustedHours > 0)
                    GameTime_GotExhausted(exhaustedHours, passedHours);
                else if (CurrentSavegame!.HoursWithoutSleep >= 24)
                    GameTime_GotTired(passedHours);
                else
                    GameTime_HoursPassed(passedHours, true);
            });
    }
}
