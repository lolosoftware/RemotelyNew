# Recette locale du 30 octobre 2026

Version testée : `0.1.1-dev`, code `c3709ac50325549b2bd59dbb2969e088fabe06cb`, paquets signés de séquence 3. Cette préversion sert à qualifier l'installation avant le déploiement réel.

## Résultats automatiques

- 34 tests Shared, 31 tests serveur, 1 test Windows et 10 tests JavaScript réussis (76 au total).
- Compilation des agents Windows x64/x86 et Linux x64, des clients d'assistance et des installateurs réussie.
- Vérification de signature du paquet Windows x64 par l'installateur en mode `--verify-only` réussie, sans installation de service.
- Image locale `ghcr.io/lolosoftware/remotelynew:0.1.1-dev` démarrée, conteneur sain.
- Volume `remotely-local_remotely_local_data` conservé lors du remplacement du conteneur. Aucune réinitialisation de compte ni de base effectuée.
- Test navigateur : accueil disponible, Viewer redirigé vers la connexion, négociation du hub anonyme refusée, routes de scripts supprimées en 404, aucune erreur JavaScript.
- ZIP Windows x64 téléchargé depuis le serveur local : 198472332 octets, SHA-256 `ED42A9F958F087C0133BBBF87DEA12AE32945786B379B9CEB7A56554A938C652`, conforme au manifeste de séquence 3.
- CI Windows/Linux du correctif : [exécution réussie](https://github.com/lolosoftware/RemotelyNew/actions/runs/41023697689).
- Publication Docker : [exécution réussie](https://github.com/lolosoftware/RemotelyNew/actions/runs/41059036100), image `ghcr.io/lolosoftware/remotelynew@sha256:96735e08176163b6b39729ebcb1feffd683ff07816654d1ff37df8c786fa7f64`. Archive compilée disponible dans la [release v0.1.1-dev](https://github.com/lolosoftware/RemotelyNew/releases/tag/v0.1.1-dev).
- Téléchargement anonyme depuis GHCR réussi avec une configuration Docker distincte, sans identifiants. Révision source de l'image contrôlée, puis recette locale basculée sur ce digest avec le même volume et manifeste de séquence 3.

## Essai Windows à effectuer sur ce PC

1. Ouvrir [le serveur de recette](http://localhost:5080), se connecter avec MFA et récupérer l'identifiant d'organisation dans la page de déploiement.
2. Télécharger à nouveau [le ZIP d'installation Windows x64](http://localhost:5080/Content/Remotely-Setup-win-x64.zip). Une ancienne copie ne contient pas la nouvelle option.
3. Extraire **tout** le ZIP dans un dossier et lancer `Remotely_Install.exe`. Accepter la demande d'élévation Windows pour installer le service.
4. Saisir `http://localhost:5080` dans **Server origin**, renseigner l'organisation et cocher **Local test (HTTP localhost only, on this computer)**. Ne pas saisir l'URL du ZIP.
5. Installer, puis vérifier que l'appareil apparaît dans Home. Ouvrir **Remote Control** depuis sa carte et confirmer une session fonctionnelle.
6. Vérifier accents, AltGr, changements de focus, écran/zoom, consentement, puis reconnexion après redémarrage du service. Pour éviter de piloter récursivement son propre écran, effectuer une seconde recette depuis un autre navigateur sur un poste pilote, avec une adresse HTTPS accessible.

Installation manuelle confirmée par l'utilisateur : installation réussie et appareil visible dans Home. Contrôle en lecture seule : `Remotely_Service` est en cours d'exécution avec démarrage automatique. L'utilisateur a ensuite confirmé une connexion Remote Control fonctionnelle, avec affichage et clavier opérationnels, après une demande explicite de vérifier les accents et caractères AltGr.

Les changements de focus, le consentement, le redémarrage Windows, l'antivirus/SmartScreen sur un poste neuf et la migration d'une sauvegarde de production restent à qualifier séparément. La session locale réussie ne vaut pas validation de tous ces scénarios.

## Gestion dans Docker Desktop

Le 30 octobre 2026, la recette a été transférée du moteur WSL `Remotely-Dev` vers le contexte Docker Desktop `desktop-linux`. Le conteneur `remotely-local-remotely-1` est sain et reste accessible sur `http://localhost:5080`. Les tests navigateur ont réussi après restauration.

Une sauvegarde du volume, effectuée serveur arrêté, est conservée localement dans `.work/desktop-migration-20261030.tar` (données sensibles, hors Git). Elle a été restaurée dans le volume Docker Desktop `remotely-local_remotely_local_data`. Le volume WSL d'origine reste conservé, avec son ancien conteneur arrêté ; ne pas redémarrer les deux instances simultanément. La copie WSL ne reçoit plus les modifications effectuées dans Docker Desktop.

Les utilitaires Start-Local et Stop-Local retrouvent désormais aussi la CLI Docker Desktop installée sous `%LOCALAPPDATA%/Programs/DockerDesktop/resources/bin`, même si elle est absente du PATH. Ils utilisent le contexte Docker actif ; sur ce PC, il s'agit de `desktop-linux`.

## Avant le réel

HTTP est limité à une sélection explicite de boucle locale : il ne convient pas à un agent sur un autre poste. Le déploiement réel doit utiliser son origine HTTPS et une image figée, après sauvegarde et restauration testée de la configuration existante selon [EXPLOITATION.md](EXPLOITATION.md).

Cette release utilise la clé de développement et des manifestes expirant le 13 novembre 2026. Préparer une publication de production signée et renouvelée, avec une clé conservée hors du dépôt. La signature des paquets ne remplace pas une signature éditeur Authenticode ; le comportement antivirus/SmartScreen doit être vérifié sur le poste pilote sans désactiver ses protections.
