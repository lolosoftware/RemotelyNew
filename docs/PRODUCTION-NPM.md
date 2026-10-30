# Production derrière Nginx Proxy Manager, sans certificat éditeur

Le domaine existant reste `https://remacces.groupe-mi.com`. Le certificat HTTPS et NPM restent en place. Aucun achat de certificat de signature de code n'est requis pour ce déploiement. Les exécutables Windows n'ont pas de signature Authenticode : Windows peut afficher « éditeur inconnu » et l'antivirus peut demander une validation. Ne pas désactiver ses protections ni créer d'exclusion générale.

## Signature gratuite des paquets

La release candidate `1.0.0-rc.1` utilise une clé RSA de production distincte de la clé de recette, séquence 100. La clé privée se trouve sur le PC de publication dans `.work/production-signing/prod.private.pem`, hors Git, dans un dossier dont les droits Windows sont limités à l'utilisateur courant et SYSTEM. Elle ne doit jamais être téléversée sur GitHub, incluse dans l'image, fournie aux agents ni copiée dans `datas`.

Avant diffusion, sauvegarder cette clé dans un coffre chiffré indépendant et vérifier sa restauration. La clé publique correspondante est `prod.public.pem`. La conserver avec son empreinte pour authentifier le premier installateur. Télécharger celui-ci depuis votre origine HTTPS et vérifier son empreinte par un canal de confiance lors du premier déploiement : un binaire sans Authenticode ne s'authentifie pas lui-même avant son exécution.

Les nouveaux installateurs contiennent la clé publique et provisionnent la confiance sur les postes. Un ancien agent de production doit être migré par l'installateur vérifié, en conservant son `ConnectionInfo.json`. Le poste de recette qui utilise la clé de développement et `localhost` n'est pas un agent déjà rattaché au domaine de production : ne pas supprimer sa confiance pour contourner le refus de migration.

La fabrication suivante est reproductible, avec une séquence strictement supérieure à la précédente et un nouveau dossier de sortie :

```powershell
./Utilities/Build-Release.ps1 -PrivateKeyPath .work/production-signing/prod.private.pem -ReleaseSequence 100 -ManifestValidityDays 90 -OutputDirectory .work/release-prod-100
./Utilities/Build-Image.ps1 -ReleaseDirectory .work/release-prod-100 -Image ghcr.io/lolosoftware/remotelynew:1.0.0-rc.1
```

`-CertificateThumbprint` est facultatif et doit être omis ici. `ManifestValidityDays` accepte de 1 à 365 jours, avec 14 par défaut. Les paquets de cette candidate expirent après 90 jours ; publier et distribuer une nouvelle release avant cette échéance. L'expiration interdit les nouvelles installations et mises à jour, sans arrêter les connexions des agents déjà installés. La signature et les contrôles de taille, hash et séquence restent obligatoires.

Pour GHCR, créer la release `v1.0.0-rc.1` avec l'asset `server-linux-x64.zip` contenant le dossier `Server` compilé, puis pousser le tag `image/v1.0.0-rc.1` sur le commit de fabrication. Le workflow existant publie l'image. Après validation du téléchargement, utiliser son digest immuable dans le Compose ; un simple changement de nom du tag de recette n'intègre pas la nouvelle clé.

## Migration de votre instance

### Sauvegarde avant migration

Sur le serveur Linux, se placer dans le répertoire du **Compose actuellement en production**, à côté du vrai dossier `datas`. Les commandes ci-dessous ne doivent pas être exécutées dans le checkout Windows du projet.

D'abord vérifier le montage ; la ligne vers `/app/AppData` doit correspondre à ce dossier :

```bash
pwd
docker inspect remotely --format '{{range .Mounts}}{{println .Source "->" .Destination}}{{end}}'
realpath ./datas
```

Si les chemins correspondent, exécuter ce bloc. Il crée un dossier privé sous votre répertoire personnel. L'export de l'image se fait avant l'arrêt pour réduire l'indisponibilité ; le serveur est ensuite arrêté pendant la sauvegarde de `datas`. Un échec après l'arrêt déclenche une tentative de redémarrage de l'ancien service.

```bash
(
  set -eu
  umask 077
  test -f ./docker-compose.yml
  test -f ./datas/Remotely.db
  test ! -L ./datas
  test ! -L ./datas/Remotely.db
  mount_source=$(docker inspect remotely --format '{{range .Mounts}}{{if eq .Destination "/app/AppData"}}{{.Source}}{{end}}{{end}}')
  test "$(realpath ./datas)" = "$(realpath "$mount_source")"
  test "$(docker inspect remotely --format '{{.State.Running}}')" = true

  remotely_backup=$(mktemp -d "$HOME/remotely-backup-$(date +%Y%m%d-%H%M%S)-XXXXXX")
  cp -p ./docker-compose.yml "$remotely_backup/"
  if test -f ./.env; then cp -p ./.env "$remotely_backup/"; fi
  docker inspect remotely > "$remotely_backup/container-inspect.json"
  image_id=$(docker inspect remotely --format '{{.Image}}')
  printf '%s\n' "$image_id" > "$remotely_backup/previous-image-id.txt"
  docker image save --output "$remotely_backup/previous-image.tar" "$image_id"

  trap 'docker start remotely >/dev/null || true' EXIT
  docker stop --time 60 remotely
  sudo tar --acls --xattrs --numeric-owner -czpf "$remotely_backup/datas.tar.gz" ./datas
  sudo tar -tzf "$remotely_backup/datas.tar.gz" >/dev/null
  (cd "$remotely_backup"; sudo sha256sum datas.tar.gz > datas.sha256)
  docker start remotely
  trap - EXIT
  printf 'Sauvegarde : %s\n' "$remotely_backup"
)
```

Vérifier ensuite `docker ps --filter name=remotely` et la connexion sur votre domaine. Copier le dossier de sauvegarde sur un stockage indépendant chiffré. `container-inspect.json`, `.env` et `datas` peuvent contenir des secrets : ne pas les publier ni les envoyer dans le chat. Si la commande signale des fichiers modifiés pendant l'archivage, vérifier qu'aucun autre service n'écrit dans `datas` et refaire une sauvegarde cohérente.

La validation de l'archive et son empreinte ne remplacent pas une restauration d'essai. Restaurer uniquement dans un **dossier neuf et isolé**, sans écraser `datas`, puis qualifier la migration selon les étapes suivantes. Pour le retour arrière effectif, arrêter les écritures, conserver aussi l'état échoué et restaurer ensemble l'image et les données sauvegardées.

### Bascule après restauration testée

Le fichier `docker-compose/compose.npm.yml` reprend la capture de votre Compose : `./datas:/app/AppData`, `127.0.0.1:5000:5000`, réseau `remotely-network` en `172.28.0.0/16`, SQLite. Il remplace uniquement le service Remotely ; il ne déploie pas NPM. Son healthcheck utilise bien le port interne 5000.

1. Conserver le Compose et les variables actuels. Vérifier le chemin absolu de `datas` avec `docker inspect remotely`. La capture ne garantit pas l'absence d'autres paramètres personnalisés.
2. Lors d'une maintenance, arrêter l'ancien conteneur puis sauvegarder **tout** le dossier `datas` (base SQLite, éventuels WAL/SHM, configuration et clés), ainsi que le Compose. Conserver l'image précédente par digest ou export Docker ; `latest` n'est pas une référence de retour arrière stable.
3. Effectuer la restauration et la qualification sur le serveur pendant la maintenance programmée ; aucun test de migration de vos données n'est prévu sur le PC de développement. Si une instance isolée est utilisée, elle doit recevoir une **copie** de la sauvegarde. Ne jamais démarrer deux serveurs sur le même dossier ou laisser une instance de recette contacter les agents de production. Si la maintenance doit être reportée, redémarrer l'ancien service.
4. Vérifier comptes/MFA, organisation, groupes, identité des appareils et options. La migration d'une sauvegarde réelle n'a pas encore été testée sur ce PC.
5. Pour la bascule, arrêter l'ancien service. Placer le nouveau Compose **dans le même répertoire que l'ancien**, à côté de `datas`. Conserver son nom de projet Compose et renseigner `REMOTELY_IMAGE` avec le digest publié et validé. Adapter les droits du dossier au compte `app` de l'image après vérification de son UID/GID, en conservant les permissions d'origine dans la sauvegarde. Ne pas rendre le dossier accessible à tous.
6. Démarrer le service et vérifier sa santé, la connexion MFA et un agent pilote. NPM continue à cibler le même port 5000, avec WebSockets et transmission de l'origine HTTPS. Si NPM utilise une autre IP source que la passerelle actuelle, vérifier et configurer cette adresse comme proxy de confiance.
7. Conserver le domaine exact dans l'installateur : `https://remacces.groupe-mi.com`, sans chemin. Ne pas sélectionner le mode HTTP local.

Le retour arrière consiste à arrêter la nouvelle instance puis restaurer ensemble le Compose, l'image précédente, les permissions et la sauvegarde de `datas`. Remettre uniquement l'ancienne image après des migrations de base ne suffit pas. Les agents déjà migrés nécessitent aussi leur sauvegarde et leur confiance correspondante.

### Commandes de bascule pendant la maintenance

Prérequis : Docker Compose v2 avec `--wait`, sauvegarde cohérente et vérifiée, clé de production sauvegardée, espace disque suffisant, fichiers `compose.npm.yml` et `.env.remotelynew` placés à côté du Compose historique et de `datas`. L'image de `.env.remotelynew` doit être figée par digest. Si des utilisateurs ont modifié la configuration depuis la sauvegarde, refaire la sauvegarde serveur arrêté avant la bascule.

```bash
# Depuis le dossier du Compose historique, après vérification des montages.
remotely_project=$(docker inspect remotely --format '{{index .Config.Labels "com.docker.compose.project"}}')
test -n "$remotely_project"

# Chargement du seul nom d'image, sans exécuter le fichier comme du code shell.
remotely_image=$(sed -n 's/^REMOTELY_IMAGE=//p' .env.remotelynew)
docker pull "$remotely_image"
docker compose --env-file .env.remotelynew -f compose.npm.yml -p "$remotely_project" config --quiet

# Lire l'identité de l'image ; ces commandes ne démarrent pas le serveur.
remotely_uid=$(docker run --rm --network none --entrypoint id "$remotely_image" -u)
remotely_gid=$(docker run --rm --network none --entrypoint id "$remotely_image" -g)
docker stop --time 60 remotely

# Le dossier datas a été vérifié ci-dessus et sauvegardé avec ses anciens droits.
sudo chown -R --no-dereference "$remotely_uid:$remotely_gid" ./datas
docker compose --env-file .env.remotelynew -f compose.npm.yml -p "$remotely_project" up -d --wait
docker ps --filter name=remotely
```

Exécuter ces commandes étape par étape et s'arrêter sur la première erreur. Si le projet Compose n'est pas renseigné sur l'ancien conteneur, relever sa configuration avant de continuer ; ne pas inventer un nom et remplacer le réseau à l'aveugle. Les opérations `id` peuvent créer puis retirer un volume anonyme Docker ; elles ne montent jamais votre `datas`.

Pendant que la maintenance est encore ouverte, contrôler connexion/MFA, groupes, appareils et paramètres, puis installer un agent pilote et effectuer une prise de main depuis le web. Pour un incident, conserver les journaux localement, arrêter la nouvelle instance et appliquer le retour arrière. Ne pas diffuser les nouveaux agents à tout le parc avant la validation du pilote.

### Exemple de retour arrière

Adapter `remotely_backup` au dossier créé lors de la sauvegarde et vérifier son chemin absolu. Les lignes suivantes s'exécutent dans le même répertoire de production que la bascule. Elles conservent les données de la tentative échouée et ne les suppriment pas.

```bash
# Choisir explicitement le vrai dossier de sauvegarde, puis vérifier l'archive.
remotely_backup=/chemin/absolu/remotely-backup-AAAAmmjj-HHMMSS-XXXXXX
(cd "$remotely_backup"; sudo sha256sum -c datas.sha256)
docker stop --time 60 remotely
docker image load --input "$remotely_backup/previous-image.tar"

# Vérifier que datas est bien le dossier attendu avant le renommage.
realpath ./datas
remotely_failed="datas.failed-$(date +%Y%m%d-%H%M%S)"
test ! -e "$remotely_failed"
sudo mv -- ./datas "$remotely_failed"
sudo tar --acls --xattrs --numeric-owner -xzpf "$remotely_backup/datas.tar.gz" -C .

# Ajouter l'image exacte sauvegardée au Compose historique, sans suivre latest.
printf 'services:\n  remotely:\n    image: %s\n' "$(cat "$remotely_backup/previous-image-id.txt")" > .rollback-image.yml
docker compose -p "$remotely_project" -f "$remotely_backup/docker-compose.yml" -f .rollback-image.yml --project-directory "$PWD" up -d --wait
```

Si le Compose historique utilise `.env`, restaurer aussi son fichier sauvegardé avant la dernière commande. La sauvegarde stocke les chemins relatifs comme `./datas` : `--project-directory "$PWD"` maintient leur résolution sur le dossier de production. Après le retour arrière, vérifier les comptes et une connexion avant de fermer la maintenance. Toute migration d'agent doit être traitée séparément avec sa propre sauvegarde ; restaurer le serveur ne rétrograde pas les agents.

Cette préparation ne modifie pas le serveur de production. La restauration et la qualification de vos données seront effectuées pendant votre maintenance programmée. Sauvegarder la clé de signature dans un coffre externe avant de déployer les nouveaux agents.
