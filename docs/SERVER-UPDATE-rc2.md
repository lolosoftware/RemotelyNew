# Mise à jour serveur 1.0.0-rc.2

Cette version corrige le cycle de fermeture des sessions identifié dans le journal du 3 novembre 2026 : `ManualResetEventSlim` déjà libéré lors de `DesktopHub.OnDisconnectedAsync`.

- Chaque session clonée possède désormais son propre signal de disponibilité. La destruction d'une session n'invalide plus les clones.
- L'attente utilise un signal asynchrone, sans thread bloqué. La fermeture est idempotente, libère les attentes en cours et ignore les notifications tardives.
- Une ancienne connexion de capture ne réinitialise plus la disponibilité d'une session déjà reconnectée ou fermée.
- Réenregistrer le même objet dans le cache ne le détruit plus.
- Les journaux de fin de flux utilisent l'identifiant capturé au début du flux, même si le navigateur change ensuite de session.

Les 40 tests serveur passent, dont neuf nouveaux cas de régression couvrant les clones, le réveil des attentes, l'annulation, les fermetures concurrentes et les anciennes connexions. Les contrôles MFA, consentement et appartenance à la session sont conservés. Ce correctif ne permet pas d'attribuer toutes les coupures initiales au serveur ; les interruptions réseau ou navigateur restent à diagnostiquer si elles persistent.

## Paquets et taille de l'image

Cette publication recompile uniquement le serveur et conserve les 36 fichiers de téléchargement de la release de production séquence 100, comparés par SHA-256. Elle n'exige pas de réinstallation des agents et conserve leurs identifiants, signatures et expiration du 29 janvier 2027. Aucune migration de schéma n'a été ajoutée.

Environ 1,36 Gio du contenu publié est constitué d'agents et de clients d'assistance Windows x64/x86 et Linux, avec des formats historiques conservés pour la migration. La nouvelle construction Docker place ce contenu dans une couche indépendante du serveur. Elle ne supprime pas ces paquets et ne promet donc pas une forte réduction de la taille totale. Les publications serveur suivantes pourront réutiliser cette couche si son contenu reste identique. Le premier passage depuis rc.1 télécharge encore la nouvelle couche ; le gain concerne surtout les publications suivantes. Docker BuildKit et le frontend Dockerfile 1.19 sont utilisés par les scripts et le workflow.

La construction rc.2 montre une couche téléchargements de 1,41 Go et une couche serveur de 111 Mo, auxquelles s'ajoute l'image de base. Les quelque 3 Go affichés par le moteur local incluent aussi le stockage containerd, qui conserve les formes compressées et décompressées ; ils ne correspondent pas au seul volume de données à télécharger. Voir la [documentation Docker sur l'espace disque containerd](https://docs.docker.com/engine/storage/containerd/#disk-space-usage).

## Appliquer pendant une maintenance

1. Conserver l'image précédente et une sauvegarde cohérente de `datas`, serveur arrêté. Les procédures détaillées se trouvent dans [PRODUCTION-NPM.md](PRODUCTION-NPM.md).
2. Dans votre configuration actuelle, remplacer uniquement la référence d'image par le digest rc.2 publié. Conserver le montage `./datas:/app/AppData`, les variables personnalisées, le port, le réseau et la configuration NPM. Le fichier de mise à jour `.env.remotelynew` ne contient que cette nouvelle référence ; ne pas remplacer un fichier `.env` personnel contenant d'autres paramètres.
3. Télécharger puis recréer le service avec les mêmes options Compose que votre installation habituelle (`-f`, `--env-file` et nom du projet s'ils sont utilisés) :

```bash
docker compose pull remotely
docker compose up -d --wait remotely
docker logs --since 5m remotely
```

4. Vérifier connexion MFA, prise de main, fermeture puis reconnexion. Le redémarrage du serveur interrompt les prises de main en cours. Les droits déjà adaptés à l'utilisateur `app` restent valables.

Ne pas modifier les paramètres CORS, les proxys de confiance ou les protections d'accès pour corriger l'exception de session. Cette publication ne touche pas à ces réglages. Aucun serveur de test supplémentaire ni aucune migration des données de production n'ont été démarrés sur le PC pour cette publication.
