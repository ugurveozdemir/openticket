for i = 1, #KEYS do
    if redis.call('EXISTS', KEYS[i]) == 1 then
        return 0
    end
end
for i = 1, #KEYS do
    redis.call('SET', KEYS[i], ARGV[1], 'PX', ARGV[2])
end
return 1
