import { useCallback, useEffect, useState } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "./ui/card";
import { Button } from "./ui/button";
import { CharacterSummary, fetchCharacters } from "../services/api";

type GenderFilter = "any" | "female" | "male";

const genderOptions: { value: GenderFilter; label: string }[] = [
  { value: "any", label: "Любой" },
  { value: "female", label: "Женский" },
  { value: "male", label: "Мужской" }
];

function describeGender(value?: string | null): string {
  switch (value?.toLowerCase()) {
    case "female":
      return "женщина";
    case "male":
      return "мужчина";
    default:
      return "не указан";
  }
}

export default function CharacterPanel() {
  const [gender, setGender] = useState<GenderFilter>("any");
  const [characters, setCharacters] = useState<CharacterSummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadCharacters = useCallback(
    async (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);
      try {
        const genderParam = gender === "any" ? undefined : gender;
        const data = await fetchCharacters(genderParam, signal);
        if (signal?.aborted) return;
        setCharacters(data);
      } catch (err) {
        if (signal?.aborted) return;
        setError(err instanceof Error ? err.message : "Неизвестная ошибка при загрузке персонажей");
        setCharacters([]);
      } finally {
        if (!signal?.aborted) {
          setLoading(false);
        }
      }
    },
    [gender]
  );

  useEffect(() => {
    const controller = new AbortController();
    void loadCharacters(controller.signal);
    return () => controller.abort();
  }, [loadCharacters]);

  const onGenderChange = (value: GenderFilter) => {
    setGender(value);
  };

  return (
    <Card className="shadow-md">
      <CardHeader>
        <CardTitle>👥 Персонажи</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4 text-sm">
        <div className="flex flex-wrap items-center gap-3">
          <label className="flex items-center gap-2 text-xs uppercase tracking-wide text-zinc-500">
            <span>Пол</span>
            <select
              value={gender}
              onChange={event => onGenderChange(event.target.value as GenderFilter)}
              className="rounded-md border border-zinc-200 bg-white px-2 py-1 text-sm focus:border-emerald-500 focus:outline-none"
            >
              {genderOptions.map(option => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
          <Button
            variant="outline"
            size="sm"
            onClick={() => void loadCharacters()}
            disabled={loading}
          >
            Обновить
          </Button>
          {loading && <span className="text-xs text-emerald-600">загрузка…</span>}
        </div>

        {error ? (
          <div className="rounded-md border border-red-200 bg-red-50 p-3 text-xs text-red-600">
            {error}
          </div>
        ) : characters.length === 0 && !loading ? (
          <div className="text-xs text-zinc-500">Персонажи не найдены для выбранного фильтра.</div>
        ) : (
          <ul className="space-y-3">
            {characters.map(character => (
              <li key={character.id} className="rounded-md border border-zinc-200 bg-white/50 p-2">
                <div className="flex items-center justify-between">
                  <span className="font-semibold text-zinc-800">{character.name}</span>
                  <span className="text-xs text-zinc-500">{describeGender(character.gender)}</span>
                </div>
                <div className="mt-1 text-xs text-zinc-500">
                  <span className="mr-2">Возраст: {character.age}</span>
                  <span className="mr-2">Статус: {character.status ?? "—"}</span>
                  <span>Локация: {character.locationName ?? "неизвестно"}</span>
                </div>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
