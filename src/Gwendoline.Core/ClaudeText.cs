namespace Gwendoline.Core;

/// <summary>
/// What the bot says about Claude Code: when Claude has finished, and when it asks for a permission.
/// Rude on purpose, the user asked for it. One line is picked at random each time.
/// </summary>
public static class ClaudeText
{
    internal static readonly string[] DoneLines =
    [
        "Gros porc, Claude a fini.",
        "Enculé, reviens vite, Claude a terminé.",
        "Lève ton gros cul, Claude a fini.",
        "Le travail est fait. Contrairement à toi, feignasse.",
        "Claude a fini pendant que tu glandais, sac à merde.",
        "Bouge-toi, tête de nœud, Claude t'attend.",
        "Claude a fini. Toi t'as fait quoi aujourd'hui, à part respirer ?",
        "Hé le parasite, Claude a bossé pour toi. Viens voir.",
        "Fini. Reviens avant que je vende ton PC, gros naze.",
        "Claude a terminé, espèce de larve. Debout.",
        "C'est fini, gros flemmard. Pose ton téléphone et va bosser.",
        "Claude a fini. Il mérite ton salaire plus que toi, connard.",
        "Allez, ramène ta gueule, Claude a fini.",
        "Fin du taf. Lâche ton kebab et reviens, gros sac.",
        "Claude a fini, abruti. Il attend ton génie, s'il existe.",
        "Debout là-dedans, bouffon, Claude a terminé.",
        "Claude a fini. Toi tu finis jamais rien, mais bon.",
        "Ton esclave numérique a fini, patron de mes deux.",
        "Claude a fini. Viens relire, au moins fais semblant d'être utile.",
        "Terminé. Arrête de scroller et reviens, déchet.",
    ];

    internal static readonly string[] AskLines =
    [
        "Hé grosse feignasse, Claude veut lancer :",
        "Réveille-toi, enculé, Claude demande la permission pour :",
        "Claude est bloqué à cause de toi, gros porc. Il veut :",
        "Ton avis, tête de con ? Claude veut faire :",
        "Lâche ton apéro, Claude attend ton feu vert pour :",
        "Claude poireaute, connard. Il veut lancer :",
    ];

    internal static readonly string[] AllowedLines =
    [
        "C'est bon, Claude bosse. Lui, au moins.",
        "Validé. Retourne glander, gros porc.",
        "Ok, Claude repart. Toi tu peux retourner à ta sieste.",
        "C'est parti. Merci d'avoir levé un doigt, feignasse.",
    ];

    internal static readonly string[] DeniedLines =
    [
        "Refusé. Claude n'y touche pas, chef.",
        "Ok, non. Claude va trouver autre chose, lui il est malin.",
        "Refusé. T'as raison pour une fois, gros naze.",
    ];

    public const string Several = "Plusieurs demandes attendent, abruti. Utilise les boutons.";
    public const string TooLate = "Trop tard, gros lent. Cette demande n'attend plus.";
    public const string Expired = "Personne n'a répondu, feignasse. La question t'attend dans le terminal.";

    public const string AllowButton = "✅ Allow";
    public const string DenyButton = "❌ Deny";

    /// <summary>Longest command shown in a permission message.</summary>
    internal const int DetailLimit = 300;

    public static string Done(string project, Random random)
    {
        return $"{Pick(DoneLines, random)}\n📁 {project}";
    }

    public static string Ask(string tool, string? detail, string project, Random random)
    {
        var what = string.IsNullOrWhiteSpace(detail) ? tool : $"{tool}: {Shorten(detail.Trim())}";
        return $"{Pick(AskLines, random)}\n{what}\n📁 {project}";
    }

    public static string Answered(bool allowed, Random random)
    {
        return Pick(allowed ? AllowedLines : DeniedLines, random);
    }

    /// <summary>Reads a typed answer: true to allow, false to deny, null when it is not one.</summary>
    public static bool? ParseAnswer(string text)
    {
        var word = text.Trim().TrimEnd('!', '.', ' ').ToLowerInvariant();
        return word switch
        {
            "oui" or "ouais" or "ok" or "okay" or "yes" or "y" or "o" or "allow" or "vas-y" or "vas y" or "go" or "valide" or "✅" or "👍" => true,
            "non" or "no" or "n" or "deny" or "refuse" or "nope" or "❌" or "👎" => false,
            _ => null,
        };
    }

    internal static string Shorten(string detail)
    {
        return detail.Length <= DetailLimit ? detail : detail[..(DetailLimit - 1)] + "…";
    }

    private static string Pick(string[] lines, Random random) => lines[random.Next(lines.Length)];
}
