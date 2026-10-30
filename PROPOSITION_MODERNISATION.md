# Proposition initiale de modernisation de Remotely

> Document de cadrage antérieur aux travaux. Consulter [le bilan de réalisation et de déploiement](docs/MODERNISATION.md) pour l’état actuel, les validations et les limites.

Date : 29 octobre 2026. Base examinée : `ec0d45d8` (17 décembre 2024), dépôt local `Remotely`, origine `https://github.com/immense/Remotely.git`.

## Orientation proposée

Créer un fork maintenu de Remotely, auto-hébergeable, conservant .NET, Blazor et le principe d'une console de contrôle entièrement web. Première cible : Windows 11 et Windows Server explicitement testés ; Linux X11 conservé, Wayland traité comme un chantier distinct. Le périmètre macOS sera déterminé par un prototype avant toute annonce de support.

Le technicien utilise un navigateur, sans extension ni logiciel à installer. La machine contrôlée exécute nécessairement un composant natif : application d'assistance portable avec consentement, ou agent installé pour l'accès permanent. Le partage d'écran d'un navigateur seul ne suffit pas à piloter tout le système, ses applications et ses écrans privilégiés.

Le parcours cible : connexion avec deux facteurs → liste des machines autorisées → choix observation/contrôle → consentement local selon la politique → session visible et révocable → journal de fin de session.

## Constats dans le clone

Il s'agit d'une lecture ciblée, pas d'un audit exhaustif ni d'une certification de sécurité. Aucun scan complet des dépendances, build ou test d'intrusion n'a été exécuté pour cette proposition. Une dépendance ancienne ne prouve pas à elle seule une vulnérabilité exploitable.

| Observation vérifiée | Conséquence pour le fork |
| --- | --- |
| `Server/Server.csproj` cible .NET 8 et référence notamment ASP.NET Core/EF Core 8.0.10, MailKit 4.8.0 et SkiaSharp 2.88.8. | Inventorier les dépendances directes, transitives et natives ; migrer vers .NET 10 LTS et des versions compatibles maintenues. |
| `Server/libman.json` utilise SignalR 8.0.7 et MessagePack 3.0.0-beta2. | Mettre à jour ensemble serveur et navigateur, figer les versions et rendre la restauration reproductible. |
| `Server/Models/SettingsModel.cs` laisse `Require2FA` et `UseHsts` à false par défaut ; l'authentification du contrôle est activée par défaut mais configurable. | Faire de l'authentification et du MFA une obligation du produit ; livrer une configuration HTTPS/HSTS adaptée au déploiement. |
| `Server/Auth/TwoFactorRequiredHandler.cs` contrôle `TwoFactorEnabled` sur le compte. | Ce contrôle ne prouve pas, à lui seul, qu'un second facteur a été présenté pendant la session courante. Auditer les connexions et appliquer une politique de session cohérente. Ce constat ne démontre pas un contournement exploitable. |
| Les événements clavier de `Server/wwwroot/src/InputEventHandlers.ts` transmettent `e.key`. Une remise à zéro sur `blur` existe déjà. | Ajouter un protocole distinguant touches physiques et texte ; compléter la récupération après changement de focus et déconnexion. |
| L'invitation sans assistance construit une URL contenant `accessKey`. | Auditer la durée et l'usage de cette clé, puis remplacer le partage par une invitation à usage unique échangée côté serveur ; éviter les secrets dans les URL. |
| `Server/wwwroot/src/CaptureProcessor.ts` décode des blocs image avec `createImageBitmap` et les dessine sur un canvas ; la capture Windows utilise SharpDX/DXGI. | Mesurer la chaîne actuelle, corriger d'abord ses limites et évaluer des liaisons graphiques maintenues. |
| La capture Linux appelle X11/Xrandr. | Ne pas annoncer une compatibilité Wayland sans implémentation et tests dédiés. |
| `Agent/Services/UpdateDownloader.cs` télécharge un fichier sans vérifier lui-même de signature. | Auditer la chaîne complète d'installation et de mise à jour ; exiger une signature vérifiée avant exécution. Ce fichier seul ne permet pas de conclure sur toute la chaîne. |

La dernière release listée par l'amont est `v2024.08.07.0819`. Le clone contient des commits postérieurs à cette release : il faut donc comparer les commits et les artefacts, pas seulement les numéros de release.

## Architecture cible

Conserver un serveur modulaire ASP.NET Core 10 avec Blazor pour l'administration et un composant TypeScript dédié à la console distante. Conserver SignalR pour les connexions des agents, la signalisation et le transport initial. Éviter une réécriture générale du frontal ou des microservices au démarrage.

Déploiement de référence : Caddy en entrée HTTPS, serveur applicatif en conteneur sans privilèges, données persistantes et sauvegardées. PostgreSQL recommandé pour une exploitation partagée ; SQLite reste possible pour une petite instance. Le mode non privilégié concerne le serveur : les privilèges nécessaires à l'agent doivent être traités séparément.

Séparer le service privilégié de l'agent et le processus de capture/interaction de la session utilisateur. Authentifier et borner leur communication locale. Les agents établissent des connexions sortantes ; aucune exposition directe d'un port de contrôle sur les postes.

Versionner le protocole et négocier les capacités. Prévoir une transition avec les agents historiques, une politique de version minimale et un retrait explicite des versions qui ne satisfont plus les exigences de sécurité.

## Sécurité et authentification

- Mot de passe et TOTP obligatoires pour la première version, avec codes de récupération à usage unique, limitation des tentatives et procédures de récupération auditées.
- Contrôler côté serveur la preuve du MFA pour la session courante avant l'ouverture d'une session distante, y compris les hubs, les API et les invitations. Couvrir aussi les reconnexions, la récupération de compte et les appareils mémorisés.
- Étudier ensuite WebAuthn/FIDO2 pour renforcer la résistance à l'hameçonnage. Le support passkey intégré à ASP.NET Core 10 est une authentification primaire, pas un second facteur prêt à l'emploi : il ne remplace pas automatiquement l'exigence de double authentification.
- Autorisations par organisation, groupe de machines et capacité : observation, interaction, fichiers, presse-papiers, terminal, administration. Les restrictions doivent être imposées côté serveur et agent, pas seulement par les boutons du navigateur.
- Réauthentification récente pour les opérations sensibles ; révocation effective des sessions et arrêt des canaux déjà ouverts lorsqu'un accès est retiré.
- Invitations courtes, à usage unique, liées au destinataire et à la machine ; pas d'accès anonyme par simple connaissance d'un identifiant de session.
- Identité propre à chaque agent, enrôlement à usage unique, rotation et révocation des secrets. Étudier les certificats d'agent selon les contraintes de distribution.
- Mises à jour signées, manifeste signé liant version et empreintes, protection contre les retours vers une version vulnérable, déploiement progressif et reprise après échec. Une empreinte fournie à côté du fichier ne remplace pas une signature de confiance.
- Consentement et indication locale de session, bouton d'arrêt accessible ; autorisations explicites pour le presse-papiers et les fichiers. Terminal et scripts désactivés par défaut dans le profil assistance.
- Journaliser acteur, machine, décision d'autorisation, horaires et opérations sensibles ; exclure secrets, frappes et contenu du presse-papiers. Enregistrement vidéo optionnel avec accès et rétention définis.
- Vérifier CSRF, XSS, origines WebSocket, CORS, proxy de confiance, isolation entre organisations, transferts de fichiers et limites de messages. Limiter tailles, débits et files d'attente pour résister aux abus.

## Clavier et souris

Proposer deux modes explicites : « Texte », qui respecte les caractères saisis localement, et « Touches physiques », qui respecte les positions et la disposition distante. Le protocole doit transporter `code`, `key`, `location`, modificateurs, répétition et événements de composition appropriés.

Traiter accents, touches mortes, AltGr, pavé numérique, dispositions FR/BE/US et IME sans doubler les caractères. Séparer les raccourcis des événements de saisie de texte et préserver leur ordre. Suivre les touches et boutons réellement pressés par chaque session ; les relâcher à la perte de focus, à la déconnexion et à l'expiration d'un délai de sécurité côté agent.

Capturer les entrées uniquement quand la surface distante a le focus. Ajouter une barre de raccourcis pour les combinaisons interceptées par le navigateur ou l'OS. Ne pas promettre la capture universelle d'Alt+Tab, des raccourcis système ou de Ctrl+Alt+Suppr. L'accès aux écrans UAC nécessite un chemin privilégié dédié et testé ; `SendInput` seul reste soumis aux niveaux d'intégrité Windows.

Corriger aussi les coordonnées avec zoom/DPI et écrans décalés, le défilement des pavés tactiles, le glisser-déposer et la libération des boutons après interruption.

## Affichage et transport

Première étape : améliorer le transport existant avec files bornées, priorité aux entrées, adaptation de qualité, abandon des images devenues obsolètes et reprise par une image complète. Préserver les dépendances entre blocs différentiels : abandonner un bloc ne doit pas laisser une zone d'écran définitivement périmée.

Console cible : sélection d'écran avec miniature, zoom 100 %, ajustement, plein écran, écran vertical et multi-écrans à DPI mixtes. Mesurer latence de bout en bout, cadence, bande passante, mémoire et charge CPU. Des profils « texte net », « équilibré » et « faible débit » rendent le réglage compréhensible.

Deuxième étape : prototype WebRTC pour la vidéo/audio et éventuellement les données d'interaction, avec encodage matériel quand disponible. Comparer H.264 et VP8 sur le parc réel avant de retenir un codec ; AV1 reste une option conditionnée au matériel et au coût d'encodage.

Prévoir STUN/TURN et tester TURN sur TLS/443 sur les réseaux filtrés ; sa disponibilité ne garantit pas le passage de tous les proxys d'entreprise. Garder le transport HTTPS/SignalR comme solution de repli. Le prototype doit démontrer un gain mesuré avant de devenir le chemin par défaut.

Définir explicitement les frontières de confiance : avec le relais SignalR, le serveur traite les données de session. Le chiffrement WebRTC ne suffit pas à annoncer une protection contre un serveur de signalisation compromis. Une telle garantie demande une liaison d'identité vérifiable entre les extrémités et une conception distincte de l'enregistrement.

Pour Wayland : prototype fondé sur les portails RemoteDesktop/ScreenCast, PipeWire et les mécanismes d'entrée pris en charge. Tester GNOME et KDE séparément ; consentement, écran verrouillé et accès sans assistance dépendent du compositeur et de sa version.

## Plan de livraison et critères de sortie

| Lot | Livrable | Validation exigée |
| --- | --- | --- |
| 0 — État de référence | Inventaire dépendances/licences, comparaison amont, build reproductible, sauvegarde et mesures initiales. | Serveur et agents compilables ; tests existants exécutés ; base restaurable ; vulnérabilités recensées avec portée et correctif. |
| 1 — Socle sécurisé | .NET 10, dépendances compatibles actualisées, MFA imposé, accès et invitations durcis, chaîne de mise à jour vérifiée. | Tests négatifs API/hubs/invitations, isolation inter-organisations, révocation active, refus des mises à jour altérées ; restauration de la version et des données répétée en préproduction. |
| 2 — Contrôle fiable | Protocole clavier versionné, modes texte/physique, corrections souris/DPI, reconnexion. | Matrice clavier FR/BE/US, accents/AltGr/IME, pertes de focus/réseau, sessions simultanées et absence de touches bloquées. |
| 3 — Console et affichage | Interface française, profils qualité, multi-écrans, métriques et adaptation du flux. | Tests à 100/125/150/200 % DPI, 1080p/4K, rotation et branchement d'écran ; comparaison aux mesures de référence. |
| 4 — Transport moderne | Prototype WebRTC puis activation progressive si concluant. | Comparaison latence/CPU/débit, tests NAT/TURN/proxys, repli fonctionnel, compatibilité navigateur et analyse de sécurité. |
| 5 — Extension des plateformes | Wayland puis éventuel agent macOS. | Matrice de versions et permissions publiée ; limitations visibles ; aucune promesse de parité avant validation. |

Le MVP comprend les lots 0 à 2 et les corrections d'affichage indispensables. Les lots suivants apportent les gains ergonomiques et de performance sans retarder la sécurisation initiale. Les délais seront estimés après le build de référence et le prototype des points difficiles ; aucune date ferme n'est crédible avant ces vérifications.

Tester d'abord Edge/Chrome et Firefox sur les versions retenues, puis Safari selon le périmètre convenu. Utiliser des machines de test autorisées, un réseau local et des profils WAN avec latence, pertes et débit limités. Publier les conditions des benchmarks plutôt qu'une promesse universelle de fluidité.

## Maintenance du fork

Conserver la licence GPL présente, les attributions et les notices ; inventorier aussi les licences des bibliothèques natives et des codecs. Définir une nouvelle identité de produit et des canaux de publication propres.

Créer le dépôt distant sous le compte ou l'organisation retenus, conserver l'amont en remote `upstream` et utiliser le fork comme `origin`. Aucun fork distant, push ou changement de remote n'a été effectué dans le cadre de cette proposition.

Mettre en place une CI serveur/agents, analyse des dépendances transitives et natives, analyse statique et secrets, SBOM, scans des images et artefacts signés. Ajouter une politique de signalement des vulnérabilités et des mises à jour régulières. Toute exception à une vulnérabilité connue doit être documentée avec responsable et échéance.

## Sources consultées

- [Releases Remotely](https://github.com/immense/Remotely/releases).
- [Politique de support .NET](https://dotnet.microsoft.com/fr-fr/platform/support/policy) : fin de support .NET 8 le 10 novembre 2026 ; .NET 10 LTS jusqu'au 14 novembre 2028.
- [Passkeys dans ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0) : possibilités et limite du support MFA intégré.
- [W3C UI Events](https://www.w3.org/TR/uievents/) : touches, positions et composition.
- [Microsoft SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) : contraintes d'intégrité Windows.
- [W3C WebRTC](https://www.w3.org/TR/webrtc/) : transport média et données temps réel.
- [XDG RemoteDesktop](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.RemoteDesktop.html) : cycle de session, consentement, capture et entrées.

Statut : proposition documentée, sans modification fonctionnelle du logiciel. Les corrections et garanties devront être démontrées par l'implémentation et les validations ci-dessus.
