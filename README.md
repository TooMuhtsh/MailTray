# MailTray

![Icônes de MailTray](docs/icones.png)

Gmail et Google Chat dans une fenêtre dédiée sous Windows, avec le nombre de mails et de messages Chat non lus sur l'icône de la zone de notification et sur celle de la barre des tâches. L'app reste dans le tray quand on ferme la fenêtre, et le compteur se met à jour en une à deux secondes.

[à écrire toi-même]

## Fonctions

- Compteur de mails non lus et de messages Chat sur l'icône du tray et de la barre des tâches.
- Plusieurs comptes Google, chacun dans sa propre session isolée.
- Une icône pour tous les comptes ou une icône par compte.
- Notifications Windows pour les nouveaux mails et les messages Chat.
- Raccourcis dans le clic droit de la barre des tâches, un par compte et un pour les paramètres.
- Mise à jour en un clic.
- Lancement avec Windows, désactivable.

## Installation

1. Installer le [runtime .NET 8 Desktop](https://dotnet.microsoft.com/download/dotnet/8.0) s'il n'est pas déjà présent.
2. Télécharger `MailTray.exe` dans les [Releases](../../releases).
3. Le placer dans un dossier à soi et le lancer.

WebView2 est déjà présent sur Windows 10 et 11.

## Mise à jour

Déposer la nouvelle version à côté de l'exe sous le nom `MailTray.update.exe`. L'app la détecte et propose de redémarrer.

## Compiler

```
dotnet publish src/MailTray/MailTray.csproj -c Release -o out
```

La compilation fonctionne aussi sous Linux.

## Confidentialité

MailTray ne collecte aucune donnée et n'envoie rien à son auteur ni à un tiers. L'app communique uniquement avec les serveurs de Google, comme un navigateur. Les sessions et les réglages restent sur le PC, dans `%APPDATA%\MailTray`.

## Modifications du système

- Une entrée `MailTray` dans `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` si « Lancer avec Windows » est activé.
- Le dossier `%APPDATA%\MailTray`.

Aucun service, aucun droit administrateur.

## Désinstallation

1. Désactiver « Lancer avec Windows » dans les paramètres.
2. Quitter l'app depuis l'icône du tray.
3. Supprimer `MailTray.exe`.
4. Supprimer le dossier `%APPDATA%\MailTray`.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- Committers and reviewers: [Alex Ramirez](https://github.com/TooMuhtsh)
- Approvers: [Alex Ramirez](https://github.com/TooMuhtsh)

Only release builds produced by the `Release` workflow of this repository, on GitHub hosted runners, are signed.

## Marques

Gmail et Google Chat sont des marques de Google LLC. MailTray est un projet indépendant, ni affilié à Google, ni approuvé par Google.

## Structure du dépôt

- `src/MailTray` contient l'app, avec l'entrée et le tray à la racine.
- `Accounts` gère la fenêtre de chaque compte et la lecture des compteurs.
- `Settings` contient la fenêtre et la page des paramètres.
- `Shell` regroupe l'intégration Windows (icône du tray, barre des tâches, Jump List).
- `Resources` contient les icônes.

## Licence

[MIT](LICENSE)
