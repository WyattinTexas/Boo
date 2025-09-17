using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;

public class BooDiceRoller : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI playerDiceText;
    public TextMeshProUGUI enemyDiceText;
    public TextMeshProUGUI battleResultText;
    public UnityEngine.UI.Button rollButton;
    
    [Header("Ghost HP")]
    public int playerGhostHP = 3;
    public int enemyGhostHP = 3;
    public TextMeshProUGUI playerHPText;
    public TextMeshProUGUI enemyHPText;
    
    [Header("Battle Results")]
    public List<int> playerDice = new List<int>();
    public List<int> enemyDice = new List<int>();
    
    private bool isRolling = false;
    
    private void Start()
    {
        if (rollButton != null)
            rollButton.onClick.AddListener(RollBattle);
            
        UpdateHPDisplay();
        UpdateDiceDisplay();
    }
    
    [ContextMenu("Roll Battle")]
    public void RollBattle()
    {
        if (isRolling) return;
        
        // Roll 3 dice for each player
        RollPlayerDice();
        RollEnemyDice();
        
        // Determine battle outcome
        BattleResult result = DetermineBattleWinner();
        
        // Apply damage
        ApplyDamage(result);
        
        // Update UI
        UpdateDiceDisplay();
        UpdateBattleResultDisplay(result);
        UpdateHPDisplay();
        
        // Check for game over
        CheckGameOver();
    }
    
    private void RollPlayerDice()
    {
        playerDice.Clear();
        for (int i = 0; i < 3; i++)
        {
            playerDice.Add(Random.Range(1, 7));
        }
        playerDice.Sort(); // Sort for easier analysis
    }
    
    private void RollEnemyDice()
    {
        enemyDice.Clear();
        for (int i = 0; i < 3; i++)
        {
            enemyDice.Add(Random.Range(1, 7));
        }
        enemyDice.Sort(); // Sort for easier analysis
    }
    
    private BattleResult DetermineBattleWinner()
    {
        // Analyze player dice
        DicePattern playerPattern = AnalyzeDicePattern(playerDice);
        DicePattern enemyPattern = AnalyzeDicePattern(enemyDice);
        
        BattleResult result = new BattleResult();
        result.playerPattern = playerPattern;
        result.enemyPattern = enemyPattern;
        
        // Determine winner based on BOO! rules
        if (playerPattern.type > enemyPattern.type)
        {
            // Player wins (higher pattern type)
            result.winner = "Player";
            result.damage = (int)playerPattern.type;
        }
        else if (enemyPattern.type > playerPattern.type)
        {
            // Enemy wins (higher pattern type)
            result.winner = "Enemy";
            result.damage = (int)enemyPattern.type;
        }
        else
        {
            // Same pattern type - apply tie-breaking rules
            result = ResolveTie(playerPattern, enemyPattern);
        }
        
        return result;
    }
    
    private BattleResult ResolveTie(DicePattern playerPattern, DicePattern enemyPattern)
    {
        BattleResult result = new BattleResult();
        result.playerPattern = playerPattern;
        result.enemyPattern = enemyPattern;
        
        switch (playerPattern.type)
        {
            case PatternType.Singles:
                // Highest individual die wins
                int playerMax = playerDice.Max();
                int enemyMax = enemyDice.Max();
                
                if (playerMax > enemyMax)
                {
                    result.winner = "Player";
                    result.damage = 1;
                }
                else if (enemyMax > playerMax)
                {
                    result.winner = "Enemy";
                    result.damage = 1;
                }
                else
                {
                    result.winner = "Tie";
                    result.damage = 0;
                }
                break;
                
            case PatternType.Doubles:
                // Higher unpaired die wins
                if (playerPattern.unpairedValue > enemyPattern.unpairedValue)
                {
                    result.winner = "Player";
                    result.damage = 2;
                }
                else if (enemyPattern.unpairedValue > playerPattern.unpairedValue)
                {
                    result.winner = "Enemy";
                    result.damage = 2;
                }
                else
                {
                    result.winner = "Tie";
                    result.damage = 0;
                }
                break;
                
            case PatternType.Triples:
                // Re-roll (or could compare triple values)
                result.winner = "Tie";
                result.damage = 0;
                break;
        }
        
        return result;
    }
    
    private DicePattern AnalyzeDicePattern(List<int> dice)
    {
        DicePattern pattern = new DicePattern();
        
        // Count occurrences of each die value
        var counts = dice.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var maxCount = counts.Values.Max();
        
        if (maxCount == 3)
        {
            // Triples
            pattern.type = PatternType.Triples;
            pattern.pairedValue = counts.First(kvp => kvp.Value == 3).Key;
        }
        else if (maxCount == 2)
        {
            // Doubles
            pattern.type = PatternType.Doubles;
            pattern.pairedValue = counts.First(kvp => kvp.Value == 2).Key;
            pattern.unpairedValue = counts.First(kvp => kvp.Value == 1).Key;
        }
        else
        {
            // Singles (all different)
            pattern.type = PatternType.Singles;
        }
        
        return pattern;
    }
    
    private void ApplyDamage(BattleResult result)
    {
        if (result.winner == "Player")
        {
            enemyGhostHP -= result.damage;
            enemyGhostHP = Mathf.Max(0, enemyGhostHP);
        }
        else if (result.winner == "Enemy")
        {
            playerGhostHP -= result.damage;
            playerGhostHP = Mathf.Max(0, playerGhostHP);
        }
    }
    
    private void UpdateDiceDisplay()
    {
        if (playerDiceText != null)
        {
            string playerText = "🎲 Player Dice 🎲\\n";
            for (int i = 0; i < playerDice.Count; i++)
            {
                playerText += $"[{playerDice[i]}] ";
            }
            playerDiceText.text = playerText;
        }
        
        if (enemyDiceText != null)
        {
            string enemyText = "👻 Enemy Dice 👻\\n";
            for (int i = 0; i < enemyDice.Count; i++)
            {
                enemyText += $"[{enemyDice[i]}] ";
            }
            enemyDiceText.text = enemyText;
        }
    }
    
    private void UpdateBattleResultDisplay(BattleResult result)
    {
        if (battleResultText == null) return;
        
        string resultText = "\\n--- BATTLE RESULT ---\\n\\n";
        
        // Show patterns
        resultText += $"Player: {GetPatternDescription(result.playerPattern)}\\n";
        resultText += $"Enemy: {GetPatternDescription(result.enemyPattern)}\\n\\n";
        
        // Show winner
        if (result.winner == "Tie")
        {
            resultText += "<color=yellow>🤝 TIE! NO DAMAGE</color>";
        }
        else if (result.winner == "Player")
        {
            resultText += $"<color=green>🎉 PLAYER WINS!</color>\\n";
            resultText += $"<color=red>Enemy takes {result.damage} damage!</color>";
        }
        else
        {
            resultText += $"<color=red>💀 ENEMY WINS!</color>\\n";
            resultText += $"<color=red>Player takes {result.damage} damage!</color>";
        }
        
        battleResultText.text = resultText;
    }
    
    private string GetPatternDescription(DicePattern pattern)
    {
        switch (pattern.type)
        {
            case PatternType.Singles:
                return "Singles (1 damage)";
            case PatternType.Doubles:
                return $"Doubles - {pattern.pairedValue}'s (2 damage)";
            case PatternType.Triples:
                return $"Triples - {pattern.pairedValue}'s (3 damage)";
            default:
                return "Unknown";
        }
    }
    
    private void UpdateHPDisplay()
    {
        if (playerHPText != null)
            playerHPText.text = $"Player HP: {GenerateHearts(playerGhostHP)}";
            
        if (enemyHPText != null)
            enemyHPText.text = $"Enemy HP: {GenerateHearts(enemyGhostHP)}";
    }
    
    private string GenerateHearts(int hp)
    {
        string hearts = "";
        for (int i = 0; i < hp; i++)
        {
            hearts += "❤️";
        }
        return hearts + $" ({hp})";
    }
    
    private void CheckGameOver()
    {
        if (playerGhostHP <= 0)
        {
            Debug.Log("GAME OVER - Enemy Wins!");
            if (battleResultText != null)
                battleResultText.text += "\\n\\n<color=red>💀 GAME OVER - ENEMY WINS! 💀</color>";
        }
        else if (enemyGhostHP <= 0)
        {
            Debug.Log("VICTORY - Player Wins!");
            if (battleResultText != null)
                battleResultText.text += "\\n\\n<color=green>🏆 VICTORY - PLAYER WINS! 🏆</color>";
        }
    }
    
    // Reset game
    [ContextMenu("Reset Game")]
    public void ResetGame()
    {
        playerGhostHP = 3;
        enemyGhostHP = 3;
        playerDice.Clear();
        enemyDice.Clear();
        
        UpdateDiceDisplay();
        UpdateHPDisplay();
        
        if (battleResultText != null)
            battleResultText.text = "Press Roll to start battle!";
    }
}

// Data structures
public enum PatternType
{
    Singles = 1,    // No matching dice - 1 damage
    Doubles = 2,    // 2 matching dice - 2 damage
    Triples = 3     // 3 matching dice - 3 damage
}

public class DicePattern
{
    public PatternType type;
    public int pairedValue;     // The value that appears multiple times
    public int unpairedValue;   // For doubles, the single die value
}

public class BattleResult
{
    public DicePattern playerPattern;
    public DicePattern enemyPattern;
    public string winner;   // "Player", "Enemy", or "Tie"
    public int damage;
}