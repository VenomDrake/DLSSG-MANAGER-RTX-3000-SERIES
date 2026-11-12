# DLSSG Mod Manager

**Lire en :** [English](../../README.md) · [Italiano](README.it.md) · [简体中文](README.zh-Hans.md) · [हिन्दी](README.hi.md) · [Español](README.es.md) · **Français** · [العربية](README.ar.md) · [বাংলা](README.bn.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [اردو](README.ur.md)

Une application de bureau Windows qui accompagne [DLSSG for SM86](https://github.com/sdli1995/dlssg_for_sm86) et [Apex Neural Rendering](https://github.com/apex-compute/ANR) : vérifier le PC et le jeu, installer un paquet vérifié du mod et conserver un historique permettant de rétablir l'état précédent.

## Crédits

- **Mod d'origine :** [DLSSG for SM86](https://github.com/sdli1995/dlssg_for_sm86), par [sdli1995](https://github.com/sdli1995) et les contributeurs du projet.
- **Adaptation RTX 20 / SM75 :** [Coldwood1026](https://github.com/Coldwood1026), cité dans les [mentions du projet d'origine](https://github.com/sdli1995/dlssg_for_sm86/blob/main/THIRD_PARTY_NOTICES.txt).
- **Ce gestionnaire :** une interface indépendante d'installation et de diagnostic. Le mérite du mod d'origine revient à ses auteurs ; le gestionnaire ne s'attribue pas leur travail et ne prétend pas bénéficier de leur approbation officielle.

## Intégration ANR — gestionnaire stable 0.4.2

[Télécharger la version stable v0.4.2](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/tag/v0.4.2): [ZIP portable](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.4.2/DLSSGModManager-Portable-win-x64.zip) · [installateur Windows](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.4.2/DLSSGModManager-Setup.msi). Le gestionnaire est une version stable. **ANR reste une bêta du projet amont, FG-V.B15** ; la mention Bêta désigne cette technologie, pas cette version du gestionnaire.

- Choisissez **Technologie de génération d’images** : SM86 d’origine (NVIDIA DLSS-G / XeSS-FG) ou **Apex Neural Rendering · Bêta**. Le gestionnaire prend en charge ANR sur **NVIDIA GTX 16 / RTX 20–50** et **AMD Radeon RX 5000/6000/7000/9000** ; **GTX 10 / Pascal n’est pas pris en charge**. Le jeu doit proposer DLSS Frame Generation natif via **Direct3D 12 ou Vulkan**.
- Ce gestionnaire prend en charge ANR uniquement sous **Windows 10/11 x64** ; **Linux, Steam Deck et Intel Arc ne font pas partie du périmètre pris en charge par cette version**. La prise en charge AMD a été vérifiée par des tests automatisés, sans essai en jeu sur du matériel AMD. Confirmez la génération effective dans le journal du jeu.
- Le gestionnaire choisit **Full** sans OptiScaler, ou **Addon** pour une installation OptiScaler compatible. Il télécharge les paquets directement depuis la publication officielle et vérifie SHA-256 ; les binaires et modèles ANR et OptiScaler **ne sont pas inclus dans le gestionnaire**. La procédure ANR vérifiée est **FG-V.B15** ; une future procédure inconnue reste bloquée. Le gestionnaire vérifie le dépôt officiel ANR au démarrage si ANR est sélectionné et chaque fois que vous choisissez ANR. Il télécharge automatiquement une nouvelle version ou indique que celle en cache est déjà la dernière. Chaque jeu est mis à jour uniquement lorsque vous cliquez sur l’action de mise à jour, après les vérifications et la sauvegarde.
- Fermez le jeu avant l’installation ou le changement. Le gestionnaire vérifie les conflits, inventorie les fichiers gérés et crée des sauvegardes récupérables avant modification. Changement de fournisseur, désactivation et restauration partagent journal et historique ; les fichiers inconnus ou modifiés ailleurs sont protégés. Addon conserve le chargeur OptiScaler existant. Utilisez **Restaurer l’état précédent** pour annuler un changement.
- Commencez hors ligne en **2×**. ANR possède ses réglages de chemin de calcul, luminance HDR et changements de scène, ainsi que son journal ; les options propres à SM86 ne s’appliquent pas. Les multiplicateurs dépendent du jeu. La confirmation du journal est un premier test, pas une garantie de stabilité prolongée. N’utilisez aucun fournisseur dans les jeux en ligne protégés par anti-triche.

Activez **NVIDIA DLSS Frame Generation** dans le jeu. Le sélecteur **2×** grisé ou verrouillé peut être normal si c’est le seul multiplicateur proposé ; cela ne prouve pas que la génération est active. Jouez un peu puis lisez le journal dans **Diagnostic**. Pour ANR, vérifiez les évaluations de génération réussies et les erreurs, pas seulement « ANR chargé ». Un journal incomplet ou trop volumineux ne permet pas de confirmer toute la session. Si la confirmation reste en attente, fermez le jeu puis relisez le journal.

**Crédits ANR :** [Apex Compute / Apex Neural Rendering](https://github.com/apex-compute/ANR) ; [OptiScaler](https://github.com/optiscaler/OptiScaler) et ses contributeurs. OptiScaler est sous GPL-3.0 ; code, binaires et modèles ANR gardent les conditions de leurs auteurs. Le gestionnaire ne modifie pas leurs licences. Voir la [question sur la licence](https://github.com/apex-compute/ANR/issues/9).

![Aperçu de l'application](../screenshot.png)

![Paramètres du mod ANR](../settings-anr.png)

## Fonctionnalités

- Organise la gestion des jeux, les réglages du mod et le diagnostic en trois pages claires, en conservant le fond bleu nuit et les touches de vert menthe d’origine.
- Au démarrage, vérifie le **paquet complet de la dernière version officielle**, avant d'analyser le matériel et le jeu. Télécharge automatiquement un nouveau paquet et indique lorsque le cache vérifié est déjà à jour.
- Compare l'identifiant de version, l'identifiant du fichier et SHA-256, y compris lorsqu'un fichier est remplacé sans changer le numéro de version. L'état hors ligne est distingué de « dernière version ».
- Vérifie Windows x64, l’architecture GPU et les pilotes selon la technologie choisie : NVIDIA uniquement pour SM86, GPU NVIDIA ou AMD compatibles pour ANR. SM86 exige la planification GPU à accélération matérielle (HAGS) ; son activation est confirmée uniquement par le journal du runtime du jeu. Le projet ANR ne précise aucune exigence HAGS.
- Accepte un dossier de jeu ou l'EXE de rendu et découvre les jeux installés dans les bibliothèques Steam. Repère les exécutables de rendu Unreal et permet de résoudre les choix ambigus.
- Examine l'architecture x64, la génération du runtime DLSS-G natif, les indices de DirectX 12 / Vulkan, les indicateurs anti-triche connus, les chargeurs existants, les processus du jeu et l'espace disponible. La présence de fichiers ne certifie pas qu'un jeu fonctionnera.
- **SM86:** Affiche le moteur, le multiplicateur, l'optimisation, le préréglage, le remplacement SR, les paramètres de flux/HUD XeSS, le chargeur et les options de journalisation. **Les réglages recommandés sont DLSS, 2×, l'optimisation à résultat exact et le remplacement SR désactivé (Off).**
- Installe ou met à jour uniquement après examen et confirmation. Prépare les fichiers vérifiés, sauvegarde l'état précédent, désactive les anciens chargeurs avant de modifier les fichiers du runtime et active le nouveau chargeur en dernier.
- Propose la désactivation et la restauration, protège les fichiers modifiés en dehors du gestionnaire et tient un journal de récupération pour les opérations interrompues. Les sauvegardes sont conservées, y compris les fichiers supplémentaires déplacés.
- **SM86:** Lit la dernière session de jeu enregistrée. Pour confirmer la génération effective d'images, exige la création réussie de la fonction NGX 11 et sa première évaluation.
- Le diagnostic ANR distingue le chargement de la génération effective et affiche le nombre d’évaluations et d’erreurs de génération pour une session enregistrée complète.
- Exporte un rapport de diagnostic local dans lequel les chemins de l'utilisateur et du jeu sont masqués. Aucun compte ni aucune télémétrie.

## Se repérer dans l’interface

- **Gestion des jeux :** choisissez un jeu, consultez le résultat de compatibilité et utilisez l’action principale d’installation ou de mise à jour en bas.
- **Réglages du mod :** retrouvez toutes les options disponibles, avec les valeurs recommandées déjà sélectionnées.
- **Diagnostic :** consultez toutes les vérifications et le journal du jeu, exportez un rapport, désactivez le mod ou rétablissez l’état précédent.

## Téléchargement et utilisation

[Télécharger la version stable v0.4.2](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/tag/v0.4.2): [ZIP portable](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.4.2/DLSSGModManager-Portable-win-x64.zip) · [installateur Windows](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.4.2/DLSSGModManager-Setup.msi).

Utilisez `DLSSGModManager.exe` depuis le paquet portable ou exécutez `DLSSGModManager-Setup.msi`, qui installe l'application pour l'utilisateur actuel. Les binaires publiés ne nécessitent pas d'installation séparée de .NET, Python ou CUDA Toolkit.

> **Jeux en ligne : risque de bannissement**
>
> L’utilisation de ce mod modifie les fichiers du jeu. Nous recommandons vivement de l’utiliser uniquement dans les jeux solo hors ligne. Les systèmes anti-triche ou les règles du jeu peuvent le considérer comme une modification non autorisée et entraîner un bannissement. Aucun contournement ni aucune protection contre les bannissements n’est fourni.

1. Ouvrez le gestionnaire et laissez-le vérifier ou télécharger le paquet officiel du mod.
2. Dans **Gestion des jeux**, choisissez un jeu Steam, un dossier de jeu ou son EXE de rendu.
3. Lisez le panneau de résultats et ouvrez **Diagnostic** pour voir toutes les vérifications. Un anti-triche détecté, un runtime non pris en charge, un chargeur inconnu en conflit ou un jeu en cours d’exécution empêchent l’installation.
4. Examinez toutes les options dans **Réglages du mod** et commencez avec **2×**. Choisissez **Installer le mod** ou **Mettre à jour le mod** dans la barre d’actions en bas, puis confirmez la destination.
5. Lancez le jeu et activez **DLSS Frame Generation** dans ses paramètres graphiques. Certains jeux nécessitent un redémarrage.
6. Dans **Diagnostic**, choisissez **Lire le journal du jeu** pour vérifier si le mod a été chargé, si la fonction a été créée et si la génération a réellement commencé.

Le gestionnaire ne modifie pas automatiquement HAGS ni les pilotes. Il ouvre les paramètres graphiques Windows et la page appropriée des pilotes NVIDIA ou AMD. Pour SM86, activez HAGS et redémarrez Windows, puis confirmez son activation dans le journal du jeu. Le projet ANR ne précise aucune exigence HAGS.

## Mettre à jour le gestionnaire

Le gestionnaire vérifie discrètement ses propres versions GitHub en arrière-plan. Un petit avis apparaît uniquement lorsqu’une version stable plus récente du gestionnaire est disponible ; les brouillons et préversions sont ignorés.

Les installations stables **0.3.1 / 0.4.1** et **0.4.0 bêta** peuvent passer à la stable **0.4.2** via **Mettre à jour et redémarrer**. Cette version est proposée sur le canal stable, sans inscription aux bêtas.

Choisissez **Mettre à jour et redémarrer** au moment qui vous convient, après la fin de l’opération en cours. Le gestionnaire sélectionne automatiquement le ZIP portable ou le MSI adapté à l’installation actuelle, le télécharge, vérifie son SHA-256 avec l’empreinte du fichier de la version officielle GitHub, se ferme, installe la mise à jour et se rouvre. Vos jeux, réglages et sauvegardes sont conservés. Les prochaines mises à jour ne nécessitent ni de visiter GitHub ni de remplacer des fichiers manuellement.

**Le passage de 0.1.0 à 0.2.0 nécessite une seule mise à jour manuelle**, car 0.1.0 ne contient pas le système de mise à jour. Fermez 0.1.0, puis lancez le nouveau MSI ou extrayez le nouveau paquet portable complet dans le dossier de votre application portable, avec le même type de paquet qu’auparavant. À partir de 0.2.0, les mises à jour suivantes du gestionnaire peuvent être installées dans l’application.

**Correction ponctuelle pour les installations MSI 0.2.0/0.2.1 :** leur mise à jour MSI depuis l’application peut échouer avec l’erreur Windows Installer **1639** ou afficher sa fenêtre d’aide. Fermez le gestionnaire, puis téléchargez et exécutez **une fois le MSI le plus récent (0.2.2 ou ultérieur)** depuis la version indiquée ci-dessus, en conservant le même dossier d’installation. Installer le MSI 0.2.1 ne corrige pas ce défaut. Une fois 0.2.2 installé, les prochaines mises à jour depuis l’application utilisent le programme de mise à jour corrigé. Les installations portables ne sont pas touchées par ce problème MSI.

Les mises à jour du gestionnaire et des mods sont indépendantes. Mettre à jour le gestionnaire ne modifie aucune installation dans les jeux. Les procédures vérifiées sont **SM86 0.4.1** et **ANR FG-V.B15**.

## Prise en charge et limites

Le gestionnaire fonctionne sous **Windows 10/11 x64**. **SM86** vise les GPU **RTX 20/30** compatibles ; la procédure vérifiée concerne le mod **0.4.1**, avec pilote **R580 ou plus récent** et runtime **310.9.16**. **ANR** prend en charge **NVIDIA GTX 16 / RTX 20–50** et **AMD Radeon RX 5000/6000/7000/9000**, exige DLSS Frame Generation natif via **Direct3D 12 ou Vulkan** et utilise la procédure vérifiée **FG-V.B15**. **GTX 10 / Pascal n’est pas pris en charge.** Télécharger une nouvelle version amont ne valide pas ses exigences : les procédures inconnues restent bloquées jusqu’à la mise à jour du gestionnaire.

**SM86:** Le jeu doit intégrer nativement DLSS Frame Generation et fonctionner avec DirectX 12. La procédure actuelle prend en charge les runtimes 310.x ; les anciennes générations sont refusées. Un multiplicateur plus élevé dépend de l'intégration du jeu. XeSS est limité à 4× ; DLSS 6× exige une intégration qui le prenne en charge.

Les indicateurs anti-triche connus bloquent l'installation. Leur absence ne prouve pas qu'un jeu est dépourvu d'anti-triche. N'utilisez pas de mods injectés dans des jeux en ligne protégés par un anti-triche.

**SM86:** Les installations manuelles existantes correspondant à la version vérifiée actuelle peuvent être prises en charge après vérification des empreintes des DLL. Un mod manuel ancien ou inconnu sans inventaire du gestionnaire est traité comme un conflit plutôt que d'être écrasé. Les installations réalisées ensuite par le gestionnaire peuvent être mises à jour et restaurées grâce à leur inventaire et à leurs sauvegardes. **ANR:** Une installation manuelle d’ANR non gérée est traitée comme un conflit.

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

Les tests utilisent des paquets synthétiques et une DLL de test x64 inoffensive contenant des métadonnées de version. Ils ne nécessitent ni jeu, ni matériel NVIDIA ou AMD, ni connexion réseau. Un test facultatif de la version en ligne est disponible avec `dotnet run --project tests/ReleaseTests -- --live`.

Pour afficher un aperçu de l'interface en lecture seule sur un jeu local :

```powershell
dotnet run --project src/DlssgManager.Desktop -- --read-only --game 'D:\Games\Example'
```

`--data-dir <path>` isole le cache et les préférences du gestionnaire pour les tests. `--read-only` désactive les modifications des fichiers du jeu.

## Publier votre fork

Créez un dépôt GitHub public et poussez cet arbre de sources. Le workflow Windows inclus compile et teste les pull requests ainsi que les envois sur la branche principale. Une étiquette comme `v0.4.2` publie aussi les binaires sous forme de version GitHub à l'aide du jeton Actions du dépôt. Aucun jeton n'est stocké dans les sources ni demandé aux utilisateurs finaux.

Consultez [CONTRIBUTING.md](../../CONTRIBUTING.md), [SECURITY.md](../../SECURITY.md) et [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md). Ce gestionnaire indépendant est distribué sous licence MIT ; le matériel du mod d'origine conserve ses propres conditions.
