# DLSSG Mod Manager

**Lire en :** [English](../../README.md) · [Italiano](README.it.md) · [简体中文](README.zh-Hans.md) · [हिन्दी](README.hi.md) · [Español](README.es.md) · **Français** · [العربية](README.ar.md) · [বাংলা](README.bn.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [اردو](README.ur.md)

Une application de bureau Windows qui accompagne [DLSSG for SM86](https://github.com/sdli1995/dlssg_for_sm86) : vérifier le PC et le jeu, installer un paquet vérifié du mod et conserver un historique permettant de rétablir l'état précédent.

## Crédits

- **Mod d'origine :** [DLSSG for SM86](https://github.com/sdli1995/dlssg_for_sm86), par [sdli1995](https://github.com/sdli1995) et les contributeurs du projet.
- **Adaptation RTX 20 / SM75 :** [Coldwood1026](https://github.com/Coldwood1026), cité dans les [mentions du projet d'origine](https://github.com/sdli1995/dlssg_for_sm86/blob/main/THIRD_PARTY_NOTICES.txt).
- **Ce gestionnaire :** une interface indépendante d'installation et de diagnostic. Le mérite du mod d'origine revient à ses auteurs ; le gestionnaire ne s'attribue pas leur travail et ne prétend pas bénéficier de leur approbation officielle.

![Aperçu de l'application](../screenshot.png)

## Fonctionnalités

- Réunit les vérifications du PC, la sélection du jeu et les réglages du mod dans une interface sombre renouvelée, en conservant les touches de vert menthe d’origine.
- Au démarrage, vérifie le **paquet complet de la dernière version officielle**, avant d'analyser le matériel et le jeu. Télécharge automatiquement un nouveau paquet et indique lorsque le cache vérifié est déjà à jour.
- Compare l'identifiant de version, l'identifiant du fichier et SHA-256, y compris lorsqu'un fichier est remplacé sans changer le numéro de version. L'état hors ligne est distingué de « dernière version ».
- Vérifie l'architecture et le pilote NVIDIA, Windows x64 et la configuration de la planification GPU. La planification n'est confirmée comme active que lorsque le journal du runtime du jeu l'indique.
- Accepte un dossier de jeu ou l'EXE de rendu et découvre les jeux installés dans les bibliothèques Steam. Repère les exécutables de rendu Unreal et permet de résoudre les choix ambigus.
- Examine l'architecture x64, la génération du runtime DLSS-G natif, les indices de DirectX 12, les indicateurs anti-triche connus, les chargeurs existants, les processus du jeu et l'espace disponible. La présence de fichiers ne certifie pas qu'un jeu fonctionnera.
- Affiche le moteur, le multiplicateur, l'optimisation, le préréglage, le remplacement SR, les paramètres de flux/HUD XeSS, le chargeur et les options de journalisation. **Les réglages recommandés sont DLSS, 2×, l'optimisation à résultat exact et le remplacement SR désactivé (Off).**
- Installe ou met à jour uniquement après examen et confirmation. Prépare les fichiers vérifiés, sauvegarde l'état précédent, désactive les anciens chargeurs avant de modifier les fichiers du runtime et active le nouveau chargeur en dernier.
- Propose la désactivation et la restauration, protège les fichiers modifiés en dehors du gestionnaire et tient un journal de récupération pour les opérations interrompues. Les sauvegardes sont conservées, y compris les fichiers supplémentaires déplacés.
- Lit la dernière session de jeu enregistrée. Pour confirmer la génération effective d'images, exige la création réussie de la fonction NGX 11 et sa première évaluation.
- Exporte un rapport de diagnostic local dans lequel les chemins de l'utilisateur et du jeu sont masqués. Aucun compte ni aucune télémétrie.

## Téléchargement et utilisation

Téléchargez la [préversion publique v0.2.1](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/tag/v0.2.1) : [ZIP portable](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.2.1/DLSSGModManager-Portable-win-x64.zip) · [programme d'installation Windows](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.2.1/DLSSGModManager-Setup.msi). Consultez [toutes les versions](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases) pour les versions suivantes.

Utilisez `DLSSGModManager.exe` depuis le paquet portable ou exécutez `DLSSGModManager-Setup.msi`, qui installe l'application pour l'utilisateur actuel. Les binaires publiés ne nécessitent pas d'installation séparée de .NET, Python ou CUDA Toolkit.

> **Jeux en ligne : risque de bannissement**
>
> L’utilisation de ce mod modifie les fichiers du jeu. Nous recommandons vivement de l’utiliser uniquement dans les jeux solo hors ligne. Les systèmes anti-triche ou les règles du jeu peuvent le considérer comme une modification non autorisée et entraîner un bannissement. Aucun contournement ni aucune protection contre les bannissements n’est fourni.

1. Ouvrez le gestionnaire et laissez-le vérifier ou télécharger le paquet officiel du mod.
2. Choisissez un jeu Steam, un dossier de jeu ou son EXE de rendu.
3. Examinez toutes les vérifications. Un anti-triche détecté, un runtime non pris en charge, un chargeur inconnu en conflit ou un jeu en cours d'exécution empêchent l'installation.
4. Examinez les options et commencez avec **2×**. Choisissez **Installer le mod** ou **Mettre à jour le mod**, puis confirmez la destination.
5. Lancez le jeu et activez **DLSS Frame Generation** dans ses paramètres graphiques. Certains jeux nécessitent un redémarrage.
6. Choisissez **Lire le journal du jeu** pour vérifier si le mod a été chargé, si la fonction a été créée et si la génération a réellement commencé.

Le gestionnaire ne modifie pas automatiquement HAGS ni les pilotes. Il ouvre les paramètres graphiques de Windows ou la page des pilotes NVIDIA et donne des instructions. Redémarrez Windows après avoir activé HAGS.

## Mettre à jour le gestionnaire

Le gestionnaire vérifie discrètement ses propres versions GitHub en arrière-plan. Un petit avis apparaît uniquement lorsqu’une version stable plus récente du gestionnaire est disponible ; les brouillons et préversions sont ignorés.

Choisissez **Mettre à jour et redémarrer** au moment qui vous convient, après la fin de l’opération en cours. Le gestionnaire sélectionne automatiquement le ZIP portable ou le MSI adapté à l’installation actuelle, le télécharge, vérifie son SHA-256 avec l’empreinte du fichier de la version officielle GitHub, se ferme, installe la mise à jour et se rouvre. Vos jeux, réglages et sauvegardes sont conservés. Les prochaines mises à jour ne nécessitent ni de visiter GitHub ni de remplacer des fichiers manuellement.

**Le passage de 0.1.0 à 0.2.0 nécessite une seule mise à jour manuelle**, car 0.1.0 ne contient pas le système de mise à jour. Fermez 0.1.0, puis lancez le nouveau MSI ou extrayez le nouveau paquet portable complet dans le dossier de votre application portable, avec le même type de paquet qu’auparavant. À partir de 0.2.0, les mises à jour suivantes du gestionnaire peuvent être installées dans l’application.

Les mises à jour du gestionnaire et du mod sont distinctes. Mettre à jour le gestionnaire n’installe aucun nouveau mod dans les jeux ; la procédure d’installation vérifiée du projet d’origine reste celle de **0.4.1**.

## Prise en charge et limites

La version 0.2.0 vise Windows 10/11 x64 et les GPU RTX 20/30 pris en charge par le projet d'origine. La procédure d'installation actuellement vérifiée concerne la version **0.4.1** du mod, avec un pilote R580 ou plus récent et le runtime 310.9.16. Le gestionnaire télécharge aussi les futures versions officielles, mais une procédure inconnue est signalée comme non prise en charge jusqu'à la mise à jour de ses vérifications ; un nouveau numéro de version ne prouve pas que les exigences sont compatibles.

Le jeu doit intégrer nativement DLSS Frame Generation et fonctionner avec DirectX 12. La procédure actuelle prend en charge les runtimes 310.x ; les anciennes générations sont refusées. Un multiplicateur plus élevé dépend de l'intégration du jeu. XeSS est limité à 4× ; DLSS 6× exige une intégration qui le prenne en charge.

Les indicateurs anti-triche connus bloquent l'installation. Leur absence ne prouve pas qu'un jeu est dépourvu d'anti-triche. N'utilisez pas de mods injectés dans des jeux en ligne protégés par un anti-triche.

Les installations manuelles existantes correspondant à la version vérifiée actuelle peuvent être prises en charge après vérification des empreintes des DLL. Un mod manuel ancien ou inconnu sans inventaire du gestionnaire est traité comme un conflit plutôt que d'être écrasé. Les installations réalisées ensuite par le gestionnaire peuvent être mises à jour et restaurées grâce à leur inventaire et à leurs sauvegardes.

La première vérification dans un journal est un test initial, pas une garantie de stabilité pendant de longues sessions ni de doublement des FPS rendus. Les images générées affichées et la latence des commandes sont des mesures différentes.

Le gestionnaire et le MSI ne sont pas signés dans cette compilation. Aucun certificat de signature n'est importé. Le paquet original 0.4.1 possède un certificat de test autosigné dont le nom diffère de celui indiqué dans son README ; la provenance du paquet est vérifiée à partir de l'empreinte du fichier sur GitHub, et non de la confiance accordée à l'éditeur par Windows.

## Langues

L'anglais est la langue source ; l'italien, le chinois simplifié, l'hindi, l'espagnol, le français, l'arabe, le bengali, le portugais, le russe et l'ourdou sont également disponibles. L'application suit une langue d'interface Windows prise en charge et permet de la changer. L'arabe et l'ourdou utilisent une disposition de droite à gauche. Les 11 fichiers de ressources ont les mêmes clés ; les relectures communautaires des traductions sont les bienvenues. Les valeurs de configuration avancées et les détails des exceptions de diagnostic peuvent conserver leurs noms techniques d'origine.

## Données locales et récupération

- Cache vérifié des paquets et préférence de langue : `%LOCALAPPDATA%\DLSSGModManager`.
- Inventaire du jeu : `.dlssg-manager.json` à côté de l'EXE de rendu.
- Sauvegardes : `.dlssg-manager-backups` à côté du même EXE. Conservez ce dossier pour préserver l'historique de restauration.
- Le fichier `.dlssg-manager.transaction.json` signale une opération interrompue ; la prochaine installation, restauration ou désactivation la récupère avant de continuer et exige que le jeu soit fermé.

Utilisez **Rétablir l’état précédent** plusieurs fois pour revenir sur les mises à jour gérées. Restaurer la première installation gérée rétablit l'état d'origine, y compris une installation manuelle précédemment prise en charge. La désactivation déplace simplement le chargeur géré hors de son nom de DLL actif. Le gestionnaire ne supprime pas les fichiers utilisateur inconnus et n'annule pas silencieusement les modifications externes des fichiers gérés.

Désinstaller le gestionnaire depuis Windows supprime l'application, pas les mods installés dans les jeux. Restaurez les jeux concernés avant de le désinstaller si vous souhaitez également retirer ces mods. Les sauvegardes des jeux et le cache téléchargé sont conservés.

## Compilation et tests

Installez le SDK .NET 10 sur Windows x64, puis exécutez :

```powershell
.\scripts\test.ps1
.\scripts\build.ps1
```

Le script de compilation publie une application autonome dans un seul fichier, un ZIP portable, un MSI et des sommes de contrôle SHA-256 dans `artifacts`. Si nécessaire, WiX 4.0.6 est installé comme outil de compilation dans `tools`. La sortie ne contient aucune DLL téléchargée du mod.

Les tests utilisent des paquets synthétiques et une DLL de test x64 inoffensive contenant des métadonnées de version. Ils ne nécessitent ni jeu, ni matériel NVIDIA, ni connexion réseau. Un test facultatif de la version en ligne est disponible avec `dotnet run --project tests/ReleaseTests -- --live`.

Pour afficher un aperçu de l'interface en lecture seule sur un jeu local :

```powershell
dotnet run --project src/DlssgManager.Desktop -- --read-only --game 'D:\Games\Example'
```

`--data-dir <path>` isole le cache et les préférences du gestionnaire pour les tests. `--read-only` désactive les modifications des fichiers du jeu.

## Publier votre fork

Créez un dépôt GitHub public et poussez cet arbre de sources. Le workflow Windows inclus compile et teste les pull requests ainsi que les envois sur la branche principale. Une étiquette comme `v0.2.1` publie aussi les binaires sous forme de version GitHub à l'aide du jeton Actions du dépôt. Aucun jeton n'est stocké dans les sources ni demandé aux utilisateurs finaux.

Consultez [CONTRIBUTING.md](../../CONTRIBUTING.md), [SECURITY.md](../../SECURITY.md) et [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md). Ce gestionnaire indépendant est distribué sous licence MIT ; le matériel du mod d'origine conserve ses propres conditions.
