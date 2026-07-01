local released = 0
for i = 1, #KEYS do
    local val = redis.call('GET', KEYS[i])
    if val == ARGV[1] then
        redis.call('DEL', KEYS[i])
        released = released + 1
    end
end
return released
