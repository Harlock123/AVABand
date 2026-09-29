namespace Angband.Core.Combat;

/// <summary>
/// Angband 4.2.5's constants.txt for critical hits — melee-critical, ranged-critical and the
/// O-combat o-melee-critical / o-ranged-critical — one constant for each line, so that
/// tools/compare_with_angband.py can check them. Levels are (cutoff, multiplier, add) for the
/// standard model, (chance, dice) for O-combat.
/// </summary>
public static class CriticalTables
{
    public const int MeleeDebuffToHit = 10;
    public const int MeleeChanceWeightScale = 1;
    public const int MeleeChanceToHitScale = 5;
    public const int MeleeChanceLevelScale = 0;
    public const int MeleeChanceToHitSkillScale = 1;
    public const int MeleeChanceOffset = -60;
    public const int MeleeChanceRange = 5000;
    public const int MeleePowerWeightScale = 1;
    public const int MeleePowerRandom = 650;
    public static readonly (int Cutoff, int Multiplier, int Add, CriticalGrade Grade)[] MeleeLevels =
    [
        (400, 2, 5, CriticalGrade.Good), (700, 2, 10, CriticalGrade.Great), (900, 3, 15, CriticalGrade.Superb),
        (1300, 3, 20, CriticalGrade.HighGreat), (-1, 4, 20, CriticalGrade.HighSuperb),
    ];

    public const int RangedDebuffToHit = 10;
    public const int RangedChanceWeightScale = 1;
    public const int RangedChanceToHitScale = 4;
    public const int RangedChanceLevelScale = 2;
    public const int RangedChanceLaunchedToHitSkillScale = 0;
    public const int RangedChanceThrownToHitSkillScale = 0;
    public const int RangedChanceOffset = 0;
    public const int RangedChanceRange = 5000;
    public const int RangedPowerWeightScale = 1;
    public const int RangedPowerRandom = 500;
    public static readonly (int Cutoff, int Multiplier, int Add, CriticalGrade Grade)[] RangedLevels =
    [
        (500, 2, 5, CriticalGrade.Good), (1000, 2, 10, CriticalGrade.Great), (-1, 3, 15, CriticalGrade.Superb),
    ];

    public const int OMeleeDebuffToHit = 10;
    public const int OMeleePowerToHitScaleNumerator = 1;
    public const int OMeleePowerToHitScaleDenominator = 3;
    public const int OMeleeChancePowerScaleNumerator = 1;
    public const int OMeleeChancePowerScaleDenominator = 1;
    public const int OMeleeChanceAddDenominator = 240;
    public static readonly (int Chance, int Dice, CriticalGrade Grade)[] OMeleeLevels =
    [
        (40, 5, CriticalGrade.HighSuperb), (12, 4, CriticalGrade.HighGreat), (3, 3, CriticalGrade.Superb),
        (2, 2, CriticalGrade.Great), (1, 1, CriticalGrade.Good),
    ];

    public const int ORangedDebuffToHit = 10;
    public const int ORangedPowerLaunchedToHitScaleNumerator = 1;
    public const int ORangedPowerLaunchedToHitScaleDenominator = 1;
    public const int ORangedPowerThrownToHitScaleNumerator = 3;
    public const int ORangedPowerThrownToHitScaleDenominator = 2;
    public const int ORangedChancePowerScaleNumerator = 1;
    public const int ORangedChancePowerScaleDenominator = 1;
    public const int ORangedChanceAddDenominator = 360;
    public static readonly (int Chance, int Dice, CriticalGrade Grade)[] ORangedLevels =
    [
        (50, 3, CriticalGrade.Superb), (10, 2, CriticalGrade.Great), (1, 1, CriticalGrade.Good),
    ];
}
