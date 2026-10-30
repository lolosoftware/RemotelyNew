# Bilan de modernisation — RemotelyNew

État au 30 octobre 2026. Le [guide d'exploitation et de reprise](EXPLOITATION.md) fait foi pour les commandes, la migration et les limites.

## Réalisé

- Dépôt autonome lolosoftware/RemotelyNew, sans rattachement GitHub de fork ni remote vers immense/Remotely.
- Migration .NET 10 et dépendances mises à jour/verrouillées ; CI de compilation, tests et audits.
- MFA obligatoire avec vérification de la session, révocation et renouvellement des cookies corrigés.
- Contrôles des hubs, appareils et sessions ; refus de transmission des commandes avant acceptation du consentement demandé.
- Scripts, Quick Scripts et Terminal supprimés de l'interface, des API, des messages de commande et de l'agent. Données historiques conservées.
- Navigation membre Home/Account/Logout et administration distincte, y compris en accès direct aux pages.
- Bouton Remote Control sur chaque carte ; recherche et filtres simplifiés, état vide explicite, affichage mobile vérifié.
- Clavier texte/physique avec composition/AltGr et relâchement du focus ; zoom, plein écran, qualité et flux bornés.
- Installateur Windows natif, paquet hors ligne vérifié, préservation de la configuration, copie de retour arrière et journaux.
- Image Docker propre au projet, incluant les paquets compilés ; Compose local et production HTTPS documentés.

## Validations

Compilation des projets maintenus et de l'installateur ; **56 tests .NET et 10 tests navigateur Node réussis** ; recette Chrome sur une copie SQLite existante ; routes d'exécution retirées en 404 ; refus d'administration pour un membre ; anciens noms de clients portables et JSON d'agent testés. Le paquet compilé a été vérifié par l'installateur en mode lecture seule. Une image a été construite et exécutée dans Docker Engine/WSL sur le PC, avec conteneur sain et accueil accessible.

## Limites

La première image 0.1.0-dev et ses paquets sont des artefacts de recette, avec clé de développement et sans certificat éditeur fourni. La qualification d'une installation Windows réelle et des environnements de production reste nécessaire. Les invitations historiques comportent encore une clé de session dans l'URL, sous contrôle d'authentification et de droits ; les invitations courtes à usage unique restent un chantier. Les validations locales et les audits de dépendances ne constituent pas un audit de sécurité exhaustif.
